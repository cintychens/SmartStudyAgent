using SmartStudyAgent.Models;
using SmartStudyAgent.Tools;

namespace SmartStudyAgent.Agents;

public sealed class MaterialAgent : IStudySubAgent
{
    private readonly StudyToolRegistry _tools;

    public MaterialAgent(StudyToolRegistry tools)
    {
        _tools = tools;
    }

    public string Name => "MaterialAgent";

    public string Description => "负责课程资料列表、资料检索和资料总结。";

    public bool CanHandle(string toolName)
    {
        return toolName is "list_materials" or "search_materials" or "summarize_material";
    }

    public Task<ToolExecutionResult> ExecuteAsync(
        ToolCallPlan plan,
        CancellationToken cancellationToken)
    {
        return _tools.ExecuteAsync(plan, cancellationToken);
    }
}
