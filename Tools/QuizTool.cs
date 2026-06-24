using System.Text.RegularExpressions;
using SmartStudyAgent.Models;
using SmartStudyAgent.Services;

namespace SmartStudyAgent.Tools;

// QuizTool 根据课程资料生成练习题、答案和解析。
public sealed class QuizTool : IStudyTool
{
    private readonly DocumentService _documents;
    private readonly ILlmService _llm;

    public QuizTool(DocumentService documents, ILlmService llm)
    {
        _documents = documents;
        _llm = llm;
    }

    public string Name => "generate_quiz";

    public string Description => "Generate practice questions from course materials.";

    public async Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken)
    {
        // 从用户问题中解析题目数量和题型
        var query = arguments.TryGetValue("query", out var value) ? value : string.Empty;
        var questionCount = ExtractQuestionCount(query);
        var selectedMaterialIds = ToolArgumentHelper.GetMaterialIds(arguments);
        string source;

        // 如果用户在聊天框选择了资料，就只基于这些资料出题。
        if (selectedMaterialIds.Count > 0)
        {
            source = await _documents.BuildCorpusAsync(selectedMaterialIds, cancellationToken);
        }
        else
        {
            var results = await _documents.SearchAsync(query, 3, cancellationToken);

            source = results.Count > 0
                ? string.Join(Environment.NewLine, results.Select(r => r.Snippet))
                : await _documents.BuildCorpusAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return new ToolExecutionResult(
                Name,
                "当前没有可用于生成练习题的资料内容。");
        }

        // 题目较多时提供更多上下文
        var maxSourceLength = questionCount >= 15 ? 9000 : 5000;
        var clipped = source.Length > maxSourceLength
            ? source[..maxSourceLength]
            : source;

        var quiz = await _llm.CompleteAsync(
            """
            你是 SmartStudyAgent 中的 PracticeAgent。

            请严格依据提供的课程资料出题。

            要求：

            1. 必须严格按照用户要求生成题目。
            2. 不允许减少题目数量。
            3. 不允许遗漏任何部分。
            4. 不允许编造资料之外的知识。
            5. 每一道题后必须给出答案和解析。
            6. 多选题允许多个正确答案。
            7. 综合题必须给出完整答案。
            8. 输出使用 Markdown 格式。
            """,

            $"""
            用户需求：

            {query}

            请严格按照用户需求出题。

            例如：

            如果用户要求：

            10道选择题、5道多选题、1道综合题

            则必须分为：

            ## 第一部分：选择题（10题）

            1.
            A.
            B.
            C.
            D.
            答案：
            解析：

            ## 第二部分：多选题（5题）

            11.
            A.
            B.
            C.
            D.
            答案：
            解析：

            ## 第三部分：综合题（1题）

            16.
            答案：
            解析：

            如果用户只要求一种题型，则全部生成这种题型。

            重要要求：

            - 必须严格满足用户要求的数量。
            - 不允许漏掉多选题和综合题。
            - 所有题目都必须来源于资料内容。
            - 每道题必须包含答案和解析。
            - 输出使用 Markdown。
            - 不要生成与课程资料无关的问题。

            课程资料：

            {clipped}
            """,

            cancellationToken);

        return new ToolExecutionResult(Name, quiz);
    }

    private static int ExtractQuestionCount(string query)
    {
        // 先识别阿拉伯数字，再识别中文数字；默认生成 5 道题。
        var digitMatch = Regex.Match(query, @"(?<count>\d{1,2})\s*(道|个|题)");
        if (digitMatch.Success && int.TryParse(digitMatch.Groups["count"].Value, out var digitCount))
        {
            return Math.Clamp(digitCount, 1, 30);
        }

        foreach (var (text, value) in ChineseNumberMap)
        {
            if (query.Contains($"{text}道", StringComparison.OrdinalIgnoreCase)
                || query.Contains($"{text}个", StringComparison.OrdinalIgnoreCase)
                || query.Contains($"{text}题", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return 5;
    }

    private static string ExtractQuestionType(string query)
    {
        // 根据用户文本判断选择题、判断题或简答题。
        if (query.Contains("选择题", StringComparison.OrdinalIgnoreCase)
            || query.Contains("单选", StringComparison.OrdinalIgnoreCase)
            || query.Contains("choice", StringComparison.OrdinalIgnoreCase))
        {
            return "选择题";
        }

        if (query.Contains("判断题", StringComparison.OrdinalIgnoreCase))
        {
            return "判断题";
        }

        if (query.Contains("简答题", StringComparison.OrdinalIgnoreCase)
            || query.Contains("问答题", StringComparison.OrdinalIgnoreCase))
        {
            return "简答题";
        }

        return "练习题";
    }

    private static string BuildTypeInstruction(string questionType)
    {
        // 为不同题型补充明确格式要求，约束 LLM 输出。
        return questionType switch
{
    "选择题" => "题型要求：全部生成选择题，每题必须有 A、B、C、D 四个选项，并且只能有一个正确答案。",
    "判断题" => "题型要求：全部生成判断题，答案只能写“正确”或“错误”，并给出一句解析。",
    "简答题" => "题型要求：全部生成简答题，答案要简洁准确，解析说明答案依据。",
    _ => "题型要求：可以混合选择题、判断题和简答题，但必须符合用户的数量要求。"
};
    }

    private static readonly IReadOnlyList<(string Text, int Value)> ChineseNumberMap = new List<(string, int)>
    {
        ("三十", 30),
        ("二十九", 29),
        ("二十八", 28),
        ("二十七", 27),
        ("二十六", 26),
        ("二十五", 25),
        ("二十四", 24),
        ("二十三", 23),
        ("二十二", 22),
        ("二十一", 21),
        ("二十", 20),
        ("十九", 19),
        ("十八", 18),
        ("十七", 17),
        ("十六", 16),
        ("十五", 15),
        ("十四", 14),
        ("十三", 13),
        ("十二", 12),
        ("十一", 11),
        ("十", 10),
        ("九", 9),
        ("八", 8),
        ("七", 7),
        ("六", 6),
        ("五", 5),
        ("四", 4),
        ("三", 3),
        ("二", 2),
        ("两", 2),
        ("一", 1)
    };
}