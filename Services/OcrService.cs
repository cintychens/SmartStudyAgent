using System.Diagnostics;
using System.Text;

namespace SmartStudyAgent.Services;

// OcrService 调用本机外部工具实现扫描版 PDF 的文字识别。
public sealed class OcrService
{
    private readonly string _tempDirectory;
    private readonly ILogger<OcrService> _logger;

    public OcrService(IWebHostEnvironment environment, ILogger<OcrService> logger)
    {
        _logger = logger;
        _tempDirectory = Path.Combine(environment.ContentRootPath, "Data", "OcrTemp");
        Directory.CreateDirectory(_tempDirectory);
    }

    public OcrStatus GetStatus()
    {
        // 检测 Tesseract、Poppler 或 MuPDF 是否已经安装并可被程序找到。
        return new OcrStatus(
            FindTool("tesseract") is not null,
            FindTool("pdftoppm") is not null,
            FindTool("mutool") is not null);
    }

    public async Task<string> ExtractPdfTextLayerAsync(
        byte[] pdfBytes,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        // pdftotext 用于读取 PDF 自带文本层，比 OCR 更快也更准确。
        var pdftotext = FindTool("pdftotext");
        if (pdftotext is null)
        {
            _logger.LogInformation("PDF text layer extraction skipped because pdftotext was not found.");
            return string.Empty;
        }

        var workDirectory = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);

        try
        {
            var inputPdf = Path.Combine(workDirectory, MakeSafeFileName(originalFileName));
            await File.WriteAllBytesAsync(inputPdf, pdfBytes, cancellationToken);

            var result = await RunProcessAsync(
                pdftotext,
                $"-layout -enc UTF-8 \"{inputPdf}\" -",
                cancellationToken);

            return Normalize(result.StdOut);
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to delete PDF text temp directory {Directory}", workDirectory);
            }
        }
    }

    public async Task<string> ExtractPdfTextAsync(
        byte[] pdfBytes,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        // OCR 流程：先把 PDF 每页渲染成图片，再用 Tesseract 识别图片文字。
        var tesseract = FindTool("tesseract");
        if (tesseract is null)
        {
            _logger.LogInformation("OCR skipped because tesseract was not found.");
            return string.Empty;
        }

        var renderer = FindTool("pdftoppm") ?? FindTool("mutool");
        if (renderer is null)
        {
            _logger.LogInformation("OCR skipped because neither pdftoppm nor mutool was found.");
            return string.Empty;
        }

        var workDirectory = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);

        try
        {
            var inputPdf = Path.Combine(workDirectory, MakeSafeFileName(originalFileName));
            await File.WriteAllBytesAsync(inputPdf, pdfBytes, cancellationToken);

            var imageFiles = await RenderPdfAsync(renderer, inputPdf, workDirectory, cancellationToken);
            if (imageFiles.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var image in imageFiles.Take(20))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = await RunOcrAsync(tesseract, image, cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text.Trim());
                    builder.AppendLine();
                }
            }

            return Normalize(builder.ToString());
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to delete OCR temp directory {Directory}", workDirectory);
            }
        }
    }

    private async Task<IReadOnlyList<string>> RenderPdfAsync(
        string renderer,
        string inputPdf,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        // Poppler 使用 pdftoppm 渲染；MuPDF 使用 mutool draw 渲染。
        var rendererName = Path.GetFileNameWithoutExtension(renderer).ToLowerInvariant();
        if (rendererName == "pdftoppm")
        {
            var prefix = Path.Combine(outputDirectory, "page");
            await RunProcessAsync(renderer, $"-png -r 180 \"{inputPdf}\" \"{prefix}\"", cancellationToken);
        }
        else
        {
            var outputPattern = Path.Combine(outputDirectory, "page-%03d.png");
            await RunProcessAsync(renderer, $"draw -r 180 -o \"{outputPattern}\" \"{inputPdf}\"", cancellationToken);
        }

        return Directory
            .EnumerateFiles(outputDirectory, "*.png")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<string> RunOcrAsync(
        string tesseract,
        string imagePath,
        CancellationToken cancellationToken)
    {
        // 优先用中文+英文识别；如果中文语言包缺失，再退回英文识别。
        var result = await RunProcessAsync(
            tesseract,
            $"\"{imagePath}\" stdout -l chi_sim+eng --psm 6",
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.StdOut)
            || !result.StdErr.Contains("Error opening data file", StringComparison.OrdinalIgnoreCase))
        {
            return result.StdOut;
        }

        result = await RunProcessAsync(
            tesseract,
            $"\"{imagePath}\" stdout -l eng --psm 6",
            cancellationToken);

        return result.StdOut;
    }

    private async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        // 统一启动外部命令并读取标准输出/错误输出，便于记录 OCR 问题。
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            _logger.LogWarning(
                "OCR command failed. File={FileName}, ExitCode={ExitCode}, Error={Error}",
                fileName,
                process.ExitCode,
                error);
        }

        return new ProcessResult(process.ExitCode, output, error);
    }

    private static string? FindTool(string name)
    {
        // 先查 PATH，再查 Windows 常见安装目录和 winget 包目录。
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var executableNames = OperatingSystem.IsWindows()
            ? new[] { $"{name}.exe", name }
            : new[] { name };

        foreach (var directory in paths.Concat(GetKnownToolDirectories(name)))
        {
            foreach (var executable in executableNames)
            {
                var path = Path.Combine(directory, executable);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> GetKnownToolDirectories(string name)
    {
        // Windows 下补充 Tesseract、Poppler、MuPDF 的常见安装位置。
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        if (name.Equals("tesseract", StringComparison.OrdinalIgnoreCase))
        {
            yield return @"C:\Program Files\Tesseract-OCR";
            yield return @"C:\Program Files (x86)\Tesseract-OCR";
            yield break;
        }

        if (!name.Equals("pdftoppm", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("pdftotext", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("mutool", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            yield break;
        }

        var packagesDirectory = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(packagesDirectory))
        {
            yield break;
        }

        foreach (var candidate in Directory.EnumerateDirectories(packagesDirectory, "*Poppler*", SearchOption.TopDirectoryOnly))
        {
            foreach (var binDirectory in Directory.EnumerateDirectories(candidate, "bin", SearchOption.AllDirectories))
            {
                yield return binDirectory;
            }
        }

        foreach (var candidate in Directory.EnumerateDirectories(packagesDirectory, "*MuPDF*", SearchOption.TopDirectoryOnly))
        {
            yield return candidate;
        }
    }

    private static string MakeSafeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(fileName.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "ocr-input.pdf" : safe;
    }

    private static string Normalize(string value)
    {
        // 清理 OCR 输出中的空字符、空行和多余空白。
        var lines = value
            .Replace("\0", string.Empty)
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join(Environment.NewLine, lines);
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}

public sealed record OcrStatus(
    bool HasTesseract,
    bool HasPopplerPdftoppm,
    bool HasMuPdfMutool)
{
    // OCR 就绪条件：Tesseract 可用，并且至少有一个 PDF 渲染工具可用。
    public bool IsReady => HasTesseract && (HasPopplerPdftoppm || HasMuPdfMutool);
}
