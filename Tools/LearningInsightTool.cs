using SmartStudyAgent.Models;
using SmartStudyAgent.Services;

namespace SmartStudyAgent.Tools;

// LearningInsightTool 用于提取关键词、核心知识点、复习提纲和易错点。
public sealed class LearningInsightTool : IStudyTool
{
    private readonly DocumentService _documents;
    private readonly ILlmService _llm;

    public LearningInsightTool(DocumentService documents, ILlmService llm)
    {
        _documents = documents;
        _llm = llm;
    }

    public string Name => "extract_learning_points";

    public string Description => "Extract keywords, key points, review outline, and learning hints from course materials.";

    public async Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken)
    {
        var goal = arguments.TryGetValue("goal", out var value) ? value : "提取课程资料学习重点";
        // 根据用户是否选择资料，决定只分析指定资料还是分析全部资料。
        var selectedMaterialIds = ToolArgumentHelper.GetMaterialIds(arguments);
        var corpus = selectedMaterialIds.Count > 0
            ? await _documents.BuildCorpusAsync(selectedMaterialIds, cancellationToken)
            : await _documents.BuildCorpusAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(corpus))
        {
            return new ToolExecutionResult(Name, "当前没有可分析的课程资料。");
        }

        // 只截取前面一部分资料交给 LLM，避免超出模型上下文限制。
        var clipped = corpus.Length > 6000 ? corpus[..6000] : corpus;
        var response = await _llm.CompleteAsync(
            "You are a study assistant. Extract keywords, key points, review outline, and learning suggestions in Chinese.",
            $"学习目标：{goal}\n\n课程资料：\n{clipped}\n\n请输出：关键词、核心知识点、复习提纲、易错点。",
            cancellationToken);

        return new ToolExecutionResult(Name, response);
    }
}
