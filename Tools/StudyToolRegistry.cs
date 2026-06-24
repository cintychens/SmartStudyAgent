using SmartStudyAgent.Models;

namespace SmartStudyAgent.Tools;

//  统一保存所有工具，负责按工具名查找并执行工具。
public sealed class StudyToolRegistry
{
    private readonly IReadOnlyDictionary<string, IStudyTool> _tools;

    public StudyToolRegistry(
        DocumentSearchTool searchTool,
        MaterialSummaryTool summaryTool,
        QuizTool quizTool,
        StudyPlanTool studyPlanTool,
        MaterialListTool listTool,
        LearningInsightTool learningInsightTool)
    {
        _tools = new IStudyTool[]
            {
                searchTool,
                summaryTool,
                quizTool,
                studyPlanTool,
                listTool,
                learningInsightTool
            }
            .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IStudyTool> ListTools() => _tools.Values.ToList();

    // 根据 ToolCallPlan 中的 ToolName 找到对应工具并执行。
    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolCallPlan plan,
        CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(plan.ToolName, out var tool))
        {
            return new ToolExecutionResult(plan.ToolName, $"Tool '{plan.ToolName}' was not found.");
        }

        return await tool.ExecuteAsync(plan.Arguments, cancellationToken);
    }
}
