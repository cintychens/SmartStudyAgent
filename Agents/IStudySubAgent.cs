using SmartStudyAgent.Models;

namespace SmartStudyAgent.Agents;

public interface IStudySubAgent
{
    string Name { get; }

    string Description { get; }

    bool CanHandle(string toolName);

    Task<ToolExecutionResult> ExecuteAsync(ToolCallPlan plan, CancellationToken cancellationToken);
}
