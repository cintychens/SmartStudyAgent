using SmartStudyAgent.Models;
using SmartStudyAgent.Tools;

namespace SmartStudyAgent.Agents;

// MaterialAgent 负责资料相关任务，例如列出资料、检索资料和总结资料。
public sealed class MaterialAgent : IStudySubAgent
{
    private readonly StudyToolRegistry _tools;

    public MaterialAgent(StudyToolRegistry tools)
    {
        _tools = tools;
    }

    public string Name => "MaterialAgent";

    public string Description => "负责课程资料列表、资料检索和资料总结。";

    // 只有资料类工具会交给 MaterialAgent 执行。
    public bool CanHandle(string toolName)
    {
        return toolName is "list_materials" or "search_materials" or "summarize_material";
    }

    // 实际工具执行仍然复用统一的工具注册表，避免重复实现工具调用逻辑。
    public Task<ToolExecutionResult> ExecuteAsync(
        ToolCallPlan plan,
        CancellationToken cancellationToken)
    {
        return _tools.ExecuteAsync(plan, cancellationToken);
    }
}
