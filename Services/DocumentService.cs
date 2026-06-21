using System.Text;
using System.Text.Json;
using SmartStudyAgent.Models;

namespace SmartStudyAgent.Services;

public sealed class DocumentService
{
    private readonly string _materialDirectory;
    private readonly string _uploadDirectory;
    private readonly OcrService _ocr;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(IWebHostEnvironment environment, OcrService ocr, ILogger<DocumentService> logger)
    {
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
        return await SaveMaterialAsync(title, content, "manual.txt", null, cancellationToken);
    }

    private async Task<CourseMaterial> SaveMaterialAsync(
        string title,
        string content,
        string originalFileName,
        byte[]? originalFileBytes,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var safeTitle = MakeSafeFileName(title);
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{safeTitle}_{id}.json";
        var path = Path.Combine(_materialDirectory, fileName);
        string? storedOriginalFileName = null;

        if (originalFileBytes is { Length: > 0 })
        {
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

        if (string.IsNullOrWhiteSpace(content))
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

        if (string.IsNullOrWhiteSpace(content)
            && Path.GetExtension(originalFileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            content = await _ocr.ExtractPdfTextAsync(originalFileBytes, originalFileName, cancellationToken);
        }

        return await SaveMaterialAsync(title, content, originalFileName, originalFileBytes, cancellationToken);
    }

    public async Task<IReadOnlyList<CourseMaterial>> ListMaterialsAsync(CancellationToken cancellationToken)
    {
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
        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null || !stored.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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

            materials.Add(new MaterialContent(stored.Id, stored.Title, stored.Content));
        }

        return materials;
    }

    public async Task<string> BuildCorpusAsync(
        IReadOnlyCollection<string> materialIds,
        CancellationToken cancellationToken)
    {
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
                return new MaterialContent(stored.Id, stored.Title, stored.Content);
            }
        }

        return null;
    }

    public async Task<string> BuildCorpusAsync(CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null)
            {
                continue;
            }

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
        var materials = new List<MaterialContent>();
        foreach (var file in Directory.EnumerateFiles(_materialDirectory, "*.json"))
        {
            var stored = await ReadStoredMaterialAsync(file, cancellationToken);
            if (stored is null)
            {
                continue;
            }

            materials.Add(new MaterialContent(stored.Id, stored.Title, stored.Content));
        }

        return materials;
    }

    private async Task<StoredMaterial?> ReadStoredMaterialAsync(string path, CancellationToken cancellationToken)
    {
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

    private static int Score(StoredMaterial material, IReadOnlyList<string> keywords)
    {
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
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(title.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "material" : safe[..Math.Min(40, safe.Length)];
    }

    private static CourseMaterial ToCourseMaterial(StoredMaterial material, string fileName)
    {
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
