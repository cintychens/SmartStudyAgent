using SmartStudyAgent.Models;

namespace SmartStudyAgent.Agents;

// 子 Agent 的统一接口，CoordinatorAgent 通过这个接口把工具任务分配给不同专长的 Agent。
public interface IStudySubAgent
{
    // 子 Agent 名称，用于 ReAct Trace 和前端时间轴展示。
    string Name { get; }

    // 子 Agent 能力说明，用于说明它负责哪类学习任务。
    string Description { get; }

    // 判断当前工具调用是否应该由这个子 Agent 处理。
    bool CanHandle(string toolName);

    // 执行 CoordinatorAgent 规划好的工具调用，并返回工具观察结果。
    Task<ToolExecutionResult> ExecuteAsync(ToolCallPlan plan, CancellationToken cancellationToken);
}
