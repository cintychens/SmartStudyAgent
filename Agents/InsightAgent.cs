using SmartStudyAgent.Models;
using SmartStudyAgent.Tools;

namespace SmartStudyAgent.Agents;

public sealed class InsightAgent : IStudySubAgent
{
    private readonly StudyToolRegistry _tools;

    public InsightAgent(StudyToolRegistry tools)
    {
        _tools = tools;
    }

    public string Name => "InsightAgent";

    public string Description => "负责提取关键词、知识点、难点和复习提纲。";

    public bool CanHandle(string toolName)
    {
        return toolName is "extract_learning_points";
    }

    public Task<ToolExecutionResult> ExecuteAsync(
        ToolCallPlan plan,
        CancellationToken cancellationToken)
    {
        return _tools.ExecuteAsync(plan, cancellationToken);
    }
}
