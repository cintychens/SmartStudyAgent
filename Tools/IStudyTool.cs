using SmartStudyAgent.Models;

namespace SmartStudyAgent.Tools;

public interface IStudyTool
{
    string Name { get; }

    string Description { get; }

    Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken);
}
