using SmartStudyAgent.Models;
using SmartStudyAgent.Tools;

namespace SmartStudyAgent.Agents;

public sealed class PlanningAgent : IStudySubAgent
{
    private readonly StudyToolRegistry _tools;

    public PlanningAgent(StudyToolRegistry tools)
    {
        _tools = tools;
    }

    public string Name => "PlanningAgent";

    public string Description => "负责根据学习目标制定学习计划。";

    public bool CanHandle(string toolName)
    {
        return toolName is "create_study_plan";
    }

    public Task<ToolExecutionResult> ExecuteAsync(
        ToolCallPlan plan,
        CancellationToken cancellationToken)
    {
        return _tools.ExecuteAsync(plan, cancellationToken);
    }
}
