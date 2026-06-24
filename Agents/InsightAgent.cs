using SmartStudyAgent.Models;
using SmartStudyAgent.Tools;

namespace SmartStudyAgent.Agents;

// InsightAgent 负责学习辅助类分析，例如关键词、知识点、难点和复习提纲。
public sealed class InsightAgent : IStudySubAgent
{
    private readonly StudyToolRegistry _tools;

    public InsightAgent(StudyToolRegistry tools)
    {
        _tools = tools;
    }

    public string Name => "InsightAgent";

    public string Description => "负责提取关键词、知识点、难点和复习提纲。";

    // 知识点提取工具由 InsightAgent 处理。
    public bool CanHandle(string toolName)
    {
        return toolName is "extract_learning_points";
    }

    // 通过统一工具注册表执行学习洞察工具。
    public Task<ToolExecutionResult> ExecuteAsync(
        ToolCallPlan plan,
        CancellationToken cancellationToken)
    {
        return _tools.ExecuteAsync(plan, cancellationToken);
    }
}
