using SmartStudyAgent.Models;
using SmartStudyAgent.Services;

namespace SmartStudyAgent.Tools;

public sealed class StudyPlanTool : IStudyTool
{
    private readonly DocumentService _documents;
    private readonly ILlmService _llm;

    public StudyPlanTool(DocumentService documents, ILlmService llm)
    {
        _documents = documents;
        _llm = llm;
    }

    public string Name => "create_study_plan";

    public string Description => "Create a personalized study plan from the user's learning goal.";

    public async Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken)
    {
        var goal = arguments.TryGetValue("goal", out var value) ? value : "学习课程资料";
        var selectedMaterialIds = ToolArgumentHelper.GetMaterialIds(arguments);
        var corpus = selectedMaterialIds.Count > 0
            ? await _documents.BuildCorpusAsync(selectedMaterialIds, cancellationToken)
            : await _documents.BuildCorpusAsync(cancellationToken);
        var clipped = corpus.Length > 4000 ? corpus[..4000] : corpus;

        var plan = await _llm.CompleteAsync(
            "You are a learning planner. Create practical daily study plans in Chinese.",
            $"Learning goal: {goal}\n\nCourse material context:\n{clipped}\n\nCreate a 3-day study plan with tasks, outputs, and review checkpoints.",
            cancellationToken);

        return new ToolExecutionResult(Name, plan);
    }
}
