using SmartStudyAgent.Models;
using SmartStudyAgent.Tools;

namespace SmartStudyAgent.Agents;

public sealed class PracticeAgent : IStudySubAgent
{
    private readonly StudyToolRegistry _tools;

    public PracticeAgent(StudyToolRegistry tools)
    {
        _tools = tools;
    }

    public string Name => "PracticeAgent";

    public string Description => "负责根据课程资料生成练习题、答案和解析。";

    public bool CanHandle(string toolName)
    {
        return toolName is "generate_quiz";
    }

    public Task<ToolExecutionResult> ExecuteAsync(
        ToolCallPlan plan,
        CancellationToken cancellationToken)
    {
        return _tools.ExecuteAsync(plan, cancellationToken);
    }
}
