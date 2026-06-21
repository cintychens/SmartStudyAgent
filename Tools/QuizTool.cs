using SmartStudyAgent.Models;
using SmartStudyAgent.Services;

namespace SmartStudyAgent.Tools;

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
        var query = arguments.TryGetValue("query", out var value) ? value : string.Empty;
        var selectedMaterialIds = ToolArgumentHelper.GetMaterialIds(arguments);
        string source;

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
            return new ToolExecutionResult(Name, "No material content is available for quiz generation.");
        }

        var clipped = source.Length > 4000 ? source[..4000] : source;
        var quiz = await _llm.CompleteAsync(
            """
            You are PracticeAgent in SmartStudyAgent.
            Create study questions in Chinese strictly from the provided material.
            Do not summarize the material.
            Every question must include an answer and a short explanation.
            """,
            $"""
            用户需求：
            {query}

            请根据下面资料生成 5 道练习题。
            输出格式必须是：

            ## 练习题
            1. 【题型】题目内容
               答案：...
               解析：...

            资料内容：
            {clipped}
            """,
            cancellationToken);

        return new ToolExecutionResult(Name, quiz);
    }
}
