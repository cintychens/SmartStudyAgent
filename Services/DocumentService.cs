using System.Text;
using System.Text.Json;
using SmartStudyAgent.Models;

namespace SmartStudyAgent.Services;

// DocumentService 负责课程资料的保存、读取、检索、预览和原始文件管理。
public sealed class DocumentService
{
    private readonly string _materialDirectory;
    private readonly string _uploadDirectory;
    private readonly OcrService _ocr;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(IWebHostEnvironment environment, OcrService ocr, ILogger<DocumentService> logger)
    {
        // 资料元数据和原始上传文件分开保存，便于预览原文件和读取提取文本。
        _logger = logger;
        _ocr = ocr;
        _materialDirectory = Path.Combine(environment.ContentRootPath, "Data", "Materials");
        _uploadDirectory = Path.Combine(environment.ContentRootPath, "Data", "Uploads");
        Directory.CreateDirectory(_materialDirectory);
        Directory.CreateDirectory(_uploadDirectory);
    }

    public async Task<CourseMaterial> SaveMaterialAsync(
        string title,
        string content,
        CancellationToken cancellationToken)
    {
        // 保存手动输入的纯文本资料。
        return await SaveMaterialAsync(title, content, "manual.txt", null, cancellationToken);
    }

    private async Task<CourseMaterial> SaveMaterialAsync(
        string title,
        string content,
        string originalFileName,
        byte[]? originalFileBytes,
        CancellationToken cancellationToken)
    {
        // 生成唯一资料 ID，并把资料正文保存成 JSON 元数据文件。
        var id = Guid.NewGuid().ToString("N");
        var safeTitle = MakeSafeFileName(title);
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{safeTitle}_{id}.json";
        var path = Path.Combine(_materialDirectory, fileName);
        string? storedOriginalFileName = null;

        if (originalFileBytes is { Length: > 0 })
        {
            // 如果是文件上传，同时保存一份原始文件供前端原样预览或下载。
            var extension = Path.GetExtension(originalFileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".bin";
            }

            storedOriginalFileName = $"{id}{extension}";
            var originalPath = Path.Combine(_uploadDirectory, storedOriginalFileName);
            await File.WriteAllBytesAsync(originalPath, originalFileBytes, cancellationToken);
        }

        var originalFileSize = originalFileBytes?.LongLength ?? Encoding.UTF8.GetByteCount(content);
        var material = new StoredMaterial(
            id,
            title.Trim(),
            content.Trim(),
            DateTimeOffset.UtcNow,
            originalFileName,
            GetFileType(originalFileName),
            storedOriginalFileName,
            originalFileSize);
        var json = JsonSerializer.Serialize(material, JsonOptions);

        await File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken);
        _logger.LogInformation("Saved course material {MaterialId} to {Path}", id, path);

        return ToCourseMaterial(material, fileName);
    }

    public async Task<CourseMaterial> SaveUploadedMaterialAsync(
        string title,
        string originalFileName,
        Stream fileStream,
        CancellationToken cancellationToken)
    {
        // 严格上传模式：如果无法提取可用文本，就直接提示上传失败。
        if (!IsSupportedFile(originalFileName))
        {
            throw new NotSupportedException("Only .pdf, .pptx, .txt and .md files are supported.");
        }

        await using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, cancellationToken);
        var originalFileBytes = buffer.ToArray();
        buffer.Position = 0;

        var content = await DocumentTextExtractor.ExtractAsync(
            originalFileName,
            buffer,
            cancellationToken);

        content = await BuildBestExtractedContentAsync(originalFileName, originalFileBytes, content, cancellationToken);

        if (IsWeakExtractedText(content))
        {
            throw new InvalidOperationException("没有从文件中读取到可用文本，请确认 PDF/PPTX 不是扫描图片。");
        }

        return await SaveMaterialAsync(title, content, originalFileName, originalFileBytes, cancellationToken);
    }

    public async Task<CourseMaterial> SaveUploadedMaterialBestEffortAsync(
        string title,
        string originalFileName,
        Stream fileStream,
        CancellationToken cancellationToken)
    {
        // 宽松上传模式：先保存原始文件，再尽力提取文本，保证资料不会因为 OCR 弱而丢失。
        if (!IsSupportedFile(originalFileName))
        {
            throw new NotSupportedException("Only .pdf, .pptx, .txt and .md files are supported.");
        }

        await using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, cancellationToken);
        var originalFileBytes = buffer.ToArray();
        var content = string.Empty;

        try
        {
            await using var extractionStream = new MemoryStream(originalFileBytes);
            content = await DocumentTextExtractor.ExtractAsync(
                originalFileName,
                extractionStream,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Uploaded file {FileName} was saved, but text extraction failed.", originalFileName);
        }

        content = await BuildBestExtractedContentAsync(originalFileName, originalFileBytes, content, cancellationToken);

        return await SaveMaterialAsync(title, content, originalFileName, originalFileBytes, cancellationToken);
    }

    public async Task<IReadOnlyList<CourseMaterial>> ListMaterialsAsync(CancellationToken cancellationToken)
    {
        // 读取所有资料 JSON 文件，并转换成列表页需要的摘要信息。
        var materials = new List<CourseMaterial>();

        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null)
            {
                continue;
            }

            materials.Add(new CourseMaterial(
                stored.Id,
                stored.Title,
                Path.GetFileName(file),
                stored.FileType,
                stored.Content.Length,
                GetDisplayFileSize(stored),
                stored.CreatedAt));
        }

        return materials.OrderByDescending(m => m.CreatedAt).ToList();
    }

    public async Task<MaterialPreview?> GetMaterialPreviewAsync(
        string id,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        // 根据资料 ID 返回一段提取文本预览，必要时会尝试修复弱文本。
        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null || !stored.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            stored = await EnsureReadableContentAsync(file, stored, cancellationToken);

            var previewLength = Math.Min(Math.Max(200, maxCharacters), stored.Content.Length);
            return new MaterialPreview(
                stored.Id,
                stored.Title,
                stored.FileType,
                stored.Content.Length,
                GetDisplayFileSize(stored),
                stored.CreatedAt,
                stored.Content[..previewLength]);
        }

        return null;
    }

    public async Task<(string Path, string ContentType, string DownloadName)?> GetOriginalFileAsync(
        string id,
        CancellationToken cancellationToken)
    {
        // 根据资料 ID 找到保存的原始上传文件，供浏览器预览或下载。
        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null || !stored.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(stored.StoredOriginalFileName))
            {
                return null;
            }

            var originalPath = Path.Combine(_uploadDirectory, stored.StoredOriginalFileName);
            if (!File.Exists(originalPath))
            {
                return null;
            }

            return (originalPath, GetContentType(stored.FileType), stored.OriginalFileName ?? stored.Title);
        }

        return null;
    }

    public async Task<CourseMaterial?> RenameMaterialAsync(
        string id,
        string title,
        CancellationToken cancellationToken)
    {
        // 只更新标题字段，不改变资料正文和原始文件。
        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null || !stored.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var renamed = stored with { Title = title.Trim() };
            var json = JsonSerializer.Serialize(renamed, JsonOptions);
            await File.WriteAllTextAsync(file, json, Encoding.UTF8, cancellationToken);
            _logger.LogInformation("Renamed course material {MaterialId}", id);
            return ToCourseMaterial(renamed, Path.GetFileName(file));
        }

        return null;
    }

    public async Task<bool> DeleteMaterialAsync(string id, CancellationToken cancellationToken)
    {
        // 删除资料元数据，同时清理它对应的原始上传文件。
        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null || !stored.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(stored.StoredOriginalFileName))
            {
                var originalPath = Path.Combine(_uploadDirectory, stored.StoredOriginalFileName);
                if (File.Exists(originalPath))
                {
                    File.Delete(originalPath);
                }
            }

            File.Delete(file);
            _logger.LogInformation("Deleted course material {MaterialId}", id);
            return true;
        }

        return false;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        // 关键词检索：统计问题关键词在标题和正文中出现的次数作为粗略分数。
        var keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (keywords.Length == 0)
        {
            keywords = new[] { query };
        }

        var results = new List<SearchResult>();
        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null)
            {
                continue;
            }

            stored = await EnsureReadableContentAsync(file, stored, cancellationToken);

            var score = Score(stored, keywords);
            if (score <= 0)
            {
                continue;
            }

            results.Add(new SearchResult(
                stored.Id,
                stored.Title,
                BuildSnippet(stored.Content, keywords),
                score));
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Title)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    public async Task<string?> GetMaterialContentAsync(string idOrTitle, CancellationToken cancellationToken)
    {
        var material = await FindMaterialContentAsync(idOrTitle, cancellationToken);
        return material?.Content;
    }

    public async Task<IReadOnlyList<MaterialContent>> GetMaterialContentsByIdsAsync(
        IReadOnlyCollection<string> materialIds,
        CancellationToken cancellationToken)
    {
        // 根据前端选择的资料 ID 批量读取正文，供总结、问答和出题工具使用。
        var idSet = materialIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (idSet.Count == 0)
        {
            return Array.Empty<MaterialContent>();
        }

        var materials = new List<MaterialContent>();
        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null || !idSet.Contains(stored.Id))
            {
                continue;
            }

            stored = await EnsureReadableContentAsync(file, stored, cancellationToken);

            materials.Add(new MaterialContent(stored.Id, stored.Title, stored.Content));
        }

        return materials;
    }

    public async Task<string> BuildCorpusAsync(
        IReadOnlyCollection<string> materialIds,
        CancellationToken cancellationToken)
    {
        // 将多份指定资料拼接成带标题的语料文本，作为 LLM 上下文。
        var materials = await GetMaterialContentsByIdsAsync(materialIds, cancellationToken);
        var builder = new StringBuilder();

        foreach (var material in materials)
        {
            if (string.IsNullOrWhiteSpace(material.Content))
            {
                continue;
            }

            builder.AppendLine($"# {material.Title}");
            builder.AppendLine(material.Content);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public async Task<MaterialContent?> FindMaterialContentAsync(string idOrTitle, CancellationToken cancellationToken)
    {
        // 按资料 ID 或标题模糊匹配单份资料。
        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null)
            {
                continue;
            }

            if (stored.Id.Equals(idOrTitle, StringComparison.OrdinalIgnoreCase)
                || stored.Title.Contains(idOrTitle, StringComparison.OrdinalIgnoreCase)
                || idOrTitle.Contains(stored.Title, StringComparison.OrdinalIgnoreCase))
            {
                stored = await EnsureReadableContentAsync(file, stored, cancellationToken);
                return new MaterialContent(stored.Id, stored.Title, stored.Content);
            }
        }

        return null;
    }

    public async Task<string> BuildCorpusAsync(CancellationToken cancellationToken)
    {
        // 构建全部资料语料，用于没有指定资料时的兜底问答。
        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null)
            {
                continue;
            }

            stored = await EnsureReadableContentAsync(file, stored, cancellationToken);

            if (string.IsNullOrWhiteSpace(stored.Content))
            {
                continue;
            }

            builder.AppendLine($"# {stored.Title}");
            builder.AppendLine(stored.Content);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public async Task<IReadOnlyList<MaterialContent>> GetAllMaterialContentsAsync(CancellationToken cancellationToken)
    {
        // 读取全部资料正文，主要供本地 RAG 切分和向量检索使用。
        var materials = new List<MaterialContent>();
        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null)
            {
                continue;
            }

            stored = await EnsureReadableContentAsync(file, stored, cancellationToken);

            materials.Add(new MaterialContent(stored.Id, stored.Title, stored.Content));
        }

        return materials;
    }

    private async Task<StoredMaterial> EnsureReadableContentAsync(
        string materialPath,
        StoredMaterial material,
        CancellationToken cancellationToken)
    {
        // 如果历史资料的 PDF 文本质量很弱，尝试用原始文件重新提取并更新缓存。
        if (!ShouldAttemptPdfRecovery(material))
        {
            return material;
        }

        var originalPath = Path.Combine(_uploadDirectory, material.StoredOriginalFileName!);
        if (!File.Exists(originalPath))
        {
            return material;
        }

        try
        {
            var originalBytes = await File.ReadAllBytesAsync(originalPath, cancellationToken);
            var recovered = await BuildBestExtractedContentAsync(
                material.OriginalFileName ?? material.Title,
                originalBytes,
                material.Content,
                cancellationToken);

            if (!IsBetterExtractedText(recovered, material.Content))
            {
                return material;
            }

            var updated = material with { Content = recovered.Trim() };
            var json = JsonSerializer.Serialize(updated, JsonOptions);
            await File.WriteAllTextAsync(materialPath, json, Encoding.UTF8, cancellationToken);
            _logger.LogInformation("Recovered readable text for material {MaterialId}", material.Id);
            return updated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to recover readable text for material {MaterialId}", material.Id);
            return material;
        }
    }

    private async Task<string> BuildBestExtractedContentAsync(
        string originalFileName,
        byte[] originalFileBytes,
        string currentContent,
        CancellationToken cancellationToken)
    {
        // 对 PDF 依次尝试内置提取、pdftotext 文本层和 OCR，选择质量最好的文本。
        var best = currentContent.Trim();
        if (!Path.GetExtension(originalFileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            || !IsWeakExtractedText(best))
        {
            return best;
        }

        var textLayer = await _ocr.ExtractPdfTextLayerAsync(originalFileBytes, originalFileName, cancellationToken);
        if (IsBetterExtractedText(textLayer, best))
        {
            best = textLayer;
        }

        if (!IsWeakExtractedText(best))
        {
            return best.Trim();
        }

        var ocrText = await _ocr.ExtractPdfTextAsync(originalFileBytes, originalFileName, cancellationToken);
        if (IsBetterExtractedText(ocrText, best))
        {
            best = ocrText;
        }

        return best.Trim();
    }

    private async Task<StoredMaterial?> ReadStoredMaterialAsync(string path, CancellationToken cancellationToken)
    {
        // 读取单个资料 JSON，损坏时记录日志并跳过，避免影响整个列表。
        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            return JsonSerializer.Deserialize<StoredMaterial>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read material file {Path}", path);
            return null;
        }
    }

    private static bool ShouldAttemptPdfRecovery(StoredMaterial material)
    {
        return material.FileType.Equals("PDF", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(material.StoredOriginalFileName)
            && IsWeakExtractedText(material.Content);
    }

    private static bool IsWeakExtractedText(string? content)
    {
        // 判断提取文本是否过短、乱码过多或可读字符比例过低。
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        var text = content.Trim();
        if (text.Length < 160)
        {
            return true;
        }

        if (text.Contains('\uFFFD') || text.Contains("锟", StringComparison.Ordinal))
        {
            return true;
        }

        var readable = text.Count(ch => char.IsLetterOrDigit(ch) || IsCjk(ch));
        if (readable < 80)
        {
            return true;
        }

        var readableRatio = readable / (double)Math.Max(1, text.Length);
        if (readableRatio < 0.35)
        {
            return true;
        }

        var distinctReadable = text
            .Where(ch => char.IsLetterOrDigit(ch) || IsCjk(ch))
            .Distinct()
            .Count();

        return distinctReadable < 20 && text.Length > 300;
    }

    private static bool IsBetterExtractedText(string? candidate, string? current)
    {
        // 比较候选文本和当前文本质量，决定是否用新提取结果替换旧结果。
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (IsWeakExtractedText(current) && !IsWeakExtractedText(candidate))
        {
            return true;
        }

        return GetTextQualityScore(candidate) > GetTextQualityScore(current) + 120;
    }

    private static int GetTextQualityScore(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        var text = content.Trim();
        var readable = text.Count(ch => char.IsLetterOrDigit(ch) || IsCjk(ch));
        var cjk = text.Count(IsCjk);
        var bad = text.Count(ch => ch == '\uFFFD' || char.IsControl(ch) && !char.IsWhiteSpace(ch));
        if (text.Contains("锟", StringComparison.Ordinal))
        {
            bad += 50;
        }

        return readable + cjk + Math.Min(text.Length, 6000) / 4 - bad * 20;
    }

    private static bool IsCjk(char value)
    {
        return value is >= '\u3400' and <= '\u9fff';
    }

    private static int Score(StoredMaterial material, IReadOnlyList<string> keywords)
    {
        // 简单关键词计分，用于本地检索排序。
        var haystack = $"{material.Title}{Environment.NewLine}{material.Content}";
        var score = 0;

        foreach (var keyword in keywords)
        {
            var index = 0;
            while ((index = haystack.IndexOf(keyword, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                score++;
                index += keyword.Length;
            }
        }

        return score;
    }

    private static string BuildSnippet(string content, IReadOnlyList<string> keywords)
    {
        // 从第一次命中的关键词附近截取一段上下文作为检索片段。
        var firstHit = keywords
            .Select(k => content.IndexOf(k, StringComparison.OrdinalIgnoreCase))
            .Where(i => i >= 0)
            .DefaultIfEmpty(0)
            .Min();

        var start = Math.Max(0, firstHit - 80);
        var length = Math.Min(320, content.Length - start);
        return content.Substring(start, length).ReplaceLineEndings(" ");
    }

    private static string MakeSafeFileName(string title)
    {
        // 清理文件名非法字符，避免保存资料 JSON 或原文件时路径出错。
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(title.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "material" : safe[..Math.Min(40, safe.Length)];
    }

    private static CourseMaterial ToCourseMaterial(StoredMaterial material, string fileName)
    {
        // 将内部存储模型转换成前端列表展示模型。
        return new CourseMaterial(
            material.Id,
            material.Title,
            fileName,
            material.FileType,
            material.Content.Length,
            GetDisplayFileSize(material),
            material.CreatedAt);
    }

    private static long GetDisplayFileSize(StoredMaterial material)
    {
        return material.OriginalFileSize > 0
            ? material.OriginalFileSize
            : Encoding.UTF8.GetByteCount(material.Content);
    }

    private static string GetFileType(string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant();
        return string.IsNullOrWhiteSpace(extension) ? "TXT" : extension;
    }

    private static bool IsSupportedFile(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() is ".pdf" or ".pptx" or ".txt" or ".md";
    }

    private static string GetContentType(string fileType)
    {
        // 根据资料类型返回浏览器可识别的 Content-Type。
        return fileType.ToUpperInvariant() switch
        {
            "PDF" => "application/pdf",
            "PPTX" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "MD" => "text/markdown; charset=utf-8",
            "TXT" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed record StoredMaterial(
        string Id,
        string Title,
        string Content,
        DateTimeOffset CreatedAt,
        string? OriginalFileName = null,
        string FileType = "TXT",
        string? StoredOriginalFileName = null,
        long OriginalFileSize = 0);
}
