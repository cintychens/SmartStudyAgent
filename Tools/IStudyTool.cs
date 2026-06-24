using SmartStudyAgent.Models;

namespace SmartStudyAgent.Tools;

// IStudyTool 是所有自定义工具的统一接口，Agent Loop 通过它调用具体学习能力。
public interface IStudyTool
{
    // 工具名称必须稳定，因为 Agent 会通过名称选择和调用工具。
    string Name { get; }

    // 工具说明用于提示 Agent 这个工具适合处理什么任务。
    string Description { get; }

    // 执行工具并返回 Observation，Observation 会进入 ReAct Trace 和最终回答生成。
    Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken);
}
