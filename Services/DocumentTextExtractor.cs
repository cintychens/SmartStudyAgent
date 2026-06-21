using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SmartStudyAgent.Services;

public static class DocumentTextExtractor
{
    public static async Task<string> ExtractAsync(
        string fileName,
        Stream fileStream,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pptx" => await ExtractPptxAsync(fileStream, cancellationToken),
            ".pdf" => await ExtractPdfAsync(fileStream, cancellationToken),
            ".txt" or ".md" => await ReadPlainTextAsync(fileStream, cancellationToken),
            _ => throw new NotSupportedException("暂时只支持上传 .pdf、.pptx、.txt、.md 文件。")
        };
    }

    private static async Task<string> ReadPlainTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return NormalizeWhitespace(await reader.ReadToEndAsync(cancellationToken));
    }

    private static async Task<string> ExtractPptxAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
        var builder = new StringBuilder();

        var slideEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => GetSlideNumber(entry.FullName))
            .ToList();

        foreach (var entry in slideEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var entryStream = entry.Open();
            var document = await XDocument.LoadAsync(entryStream, LoadOptions.None, cancellationToken);
            var texts = document.Descendants()
                .Where(e => e.Name.LocalName == "t")
                .Select(e => e.Value.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text));

            builder.AppendLine($"## Slide {GetSlideNumber(entry.FullName)}");
            builder.AppendLine(string.Join(" ", texts));
            builder.AppendLine();
        }

        return NormalizeWhitespace(builder.ToString());
    }

    private static async Task<string> ExtractPdfAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        var latin = Encoding.Latin1.GetString(bytes);
        var cmap = BuildToUnicodeMap(latin);
        var builder = new StringBuilder();

        foreach (Match match in PdfStreamRegex.Matches(latin))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rawStreamText = match.Groups[1].Value.Trim('\r', '\n');
            var rawBytes = Encoding.Latin1.GetBytes(rawStreamText);
            var decoded = TryInflate(rawBytes) ?? rawBytes;
            var content = Encoding.Latin1.GetString(decoded);

            ExtractPdfTextOperators(content, cmap, builder);
        }

        if (builder.Length == 0)
        {
            foreach (Match match in PdfLiteralRegex.Matches(latin))
            {
                var value = DecodePdfLiteral(match.Groups[1].Value);
                if (IsUsefulText(value))
                {
                    AppendCandidate(builder, value);
                }
            }
        }

        return NormalizeWhitespace(builder.ToString());
    }

    private static Dictionary<int, string> BuildToUnicodeMap(string pdfText)
    {
        var map = new Dictionary<int, string>();

        foreach (Match streamMatch in PdfStreamRegex.Matches(pdfText))
        {
            var streamText = streamMatch.Groups[1].Value;
            if (!streamText.Contains("beginbfchar", StringComparison.OrdinalIgnoreCase)
                && !streamText.Contains("beginbfrange", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match mapMatch in CMapEntryRegex.Matches(streamText))
            {
                var source = Convert.ToInt32(mapMatch.Groups[1].Value, 16);
                var target = Convert.ToInt32(mapMatch.Groups[2].Value, 16);
                map[source] = char.ConvertFromUtf32(target);
            }
        }

        return map;
    }

    private static void ExtractPdfTextOperators(
        string content,
        IReadOnlyDictionary<int, string> cmap,
        StringBuilder builder)
    {
        foreach (Match match in PdfTjRegex.Matches(content))
        {
            AppendCandidate(builder, DecodePdfString(match.Groups[1].Value, cmap));
        }

        foreach (Match match in PdfHexTjRegex.Matches(content))
        {
            AppendCandidate(builder, DecodePdfHexString(match.Groups[1].Value, cmap));
        }

        foreach (Match match in PdfTjArrayRegex.Matches(content))
        {
            var arrayContent = match.Groups[1].Value;
            var literalParts = PdfLiteralRegex.Matches(arrayContent)
                .Select(part => DecodePdfString(part.Groups[1].Value, cmap));
            var hexParts = PdfHexStringRegex.Matches(arrayContent)
                .Select(part => DecodePdfHexString(part.Groups[1].Value, cmap));
            var parts = literalParts.Concat(hexParts);
            AppendCandidate(builder, string.Concat(parts));
        }
    }

    private static string DecodePdfHexString(string value, IReadOnlyDictionary<int, string> cmap)
    {
        var clean = Regex.Replace(value, @"\s+", string.Empty);
        if (clean.Length % 2 == 1)
        {
            clean += "0";
        }

        var bytes = new byte[clean.Length / 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = Convert.ToByte(clean.Substring(index * 2, 2), 16);
        }

        if (cmap.Count > 0 && bytes.Length >= 2)
        {
            var builder = new StringBuilder();
            for (var index = 0; index + 1 < bytes.Length; index += 2)
            {
                var code = (bytes[index] << 8) + bytes[index + 1];
                if (cmap.TryGetValue(code, out var mapped))
                {
                    builder.Append(mapped);
                }
            }

            if (builder.Length > 0)
            {
                return builder.ToString();
            }
        }

        var utf8 = Encoding.UTF8.GetString(bytes);
        if (LooksLikeText(utf8))
        {
            return utf8;
        }

        return Encoding.Latin1.GetString(bytes);
    }

    private static string DecodePdfString(string value, IReadOnlyDictionary<int, string> cmap)
    {
        var unescaped = DecodePdfLiteralBytes(value);
        if (cmap.Count > 0 && unescaped.Length >= 2)
        {
            var builder = new StringBuilder();
            for (var index = 0; index + 1 < unescaped.Length; index += 2)
            {
                var code = (unescaped[index] << 8) + unescaped[index + 1];
                if (cmap.TryGetValue(code, out var mapped))
                {
                    builder.Append(mapped);
                }
            }

            if (builder.Length > 0)
            {
                return builder.ToString();
            }
        }

        return DecodePdfLiteral(value);
    }

    private static string DecodePdfLiteral(string value)
    {
        var bytes = DecodePdfLiteralBytes(value);
        var utf8 = Encoding.UTF8.GetString(bytes);
        return LooksLikeText(utf8) ? utf8 : Encoding.Latin1.GetString(bytes);
    }

    private static byte[] DecodePdfLiteralBytes(string value)
    {
        var output = new List<byte>();

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current != '\\')
            {
                output.Add((byte)current);
                continue;
            }

            if (++index >= value.Length)
            {
                break;
            }

            current = value[index];
            switch (current)
            {
                case 'n':
                    output.Add((byte)'\n');
                    break;
                case 'r':
                    output.Add((byte)'\r');
                    break;
                case 't':
                    output.Add((byte)'\t');
                    break;
                case 'b':
                    output.Add(8);
                    break;
                case 'f':
                    output.Add(12);
                    break;
                case '(':
                case ')':
                case '\\':
                    output.Add((byte)current);
                    break;
                default:
                    if (current is >= '0' and <= '7')
                    {
                        var octal = current.ToString();
                        for (var count = 0; count < 2 && index + 1 < value.Length && value[index + 1] is >= '0' and <= '7'; count++)
                        {
                            octal += value[++index];
                        }

                        output.Add(Convert.ToByte(octal, 8));
                    }

                    break;
            }
        }

        return output.ToArray();
    }

    private static byte[]? TryInflate(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            using var deflate = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var printable = value.Count(ch => !char.IsControl(ch) || char.IsWhiteSpace(ch));
        return printable >= Math.Max(1, value.Length * 0.7);
    }

    private static void AppendCandidate(StringBuilder builder, string value)
    {
        value = Regex.Replace(value.Trim(), @"\s+", " ");
        if (IsUsefulText(value))
        {
            builder.AppendLine(value);
        }
    }

    private static bool IsUsefulText(string value)
    {
        if (!LooksLikeText(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length < 3 || trimmed.Contains('�'))
        {
            return false;
        }

        var meaningful = trimmed.Count(ch =>
            char.IsLetterOrDigit(ch)
            || IsCjk(ch)
            || char.IsWhiteSpace(ch)
            || "，。！？；：、,.!?;:()（）-_/".Contains(ch));

        return meaningful >= Math.Max(3, trimmed.Length * 0.75);
    }

    private static bool IsCjk(char value)
    {
        return value is >= '\u3400' and <= '\u9fff';
    }

    private static string NormalizeWhitespace(string value)
    {
        var lines = value
            .Replace("\0", string.Empty)
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => Regex.Replace(line.Trim(), @"\s+", " "))
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join(Environment.NewLine, lines);
    }

    private static int GetSlideNumber(string path)
    {
        var match = Regex.Match(path, @"slide(\d+)\.xml", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private static readonly Regex PdfStreamRegex = new(
        @"stream\r?\n(.*?)\r?\nendstream",
        RegexOptions.Singleline);

    private static readonly Regex PdfLiteralRegex = new(
        @"\(((?:\\.|[^\\()])*)\)");

    private static readonly Regex PdfTjRegex = new(
        @"\(((?:\\.|[^\\()])*)\)\s*Tj",
        RegexOptions.Singleline);

    private static readonly Regex PdfHexTjRegex = new(
        @"<([0-9A-Fa-f\s]+)>\s*Tj",
        RegexOptions.Singleline);

    private static readonly Regex PdfTjArrayRegex = new(
        @"\[(.*?)\]\s*TJ",
        RegexOptions.Singleline);

    private static readonly Regex PdfHexStringRegex = new(
        @"<([0-9A-Fa-f\s]+)>",
        RegexOptions.Singleline);

    private static readonly Regex CMapEntryRegex = new(
        @"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>");
}
