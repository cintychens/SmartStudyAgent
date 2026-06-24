using SmartStudyAgent.Models;
using SmartStudyAgent.Tools;

namespace SmartStudyAgent.Agents;

// PlanningAgent 负责把用户学习目标转化成按天安排的学习计划。
public sealed class PlanningAgent : IStudySubAgent
{
    private readonly StudyToolRegistry _tools;

    public PlanningAgent(StudyToolRegistry tools)
    {
        _tools = tools;
    }

    public string Name => "PlanningAgent";

    public string Description => "负责根据学习目标制定学习计划。";

    // 学习计划工具由 PlanningAgent 处理。
    public bool CanHandle(string toolName)
    {
        return toolName is "create_study_plan";
    }

    // 复用工具注册表完成实际计划生成。
    public Task<ToolExecutionResult> ExecuteAsync(
        ToolCallPlan plan,
        CancellationToken cancellationToken)
    {
        return _tools.ExecuteAsync(plan, cancellationToken);
    }
}
