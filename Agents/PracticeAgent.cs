using SmartStudyAgent.Models;
using SmartStudyAgent.Tools;

namespace SmartStudyAgent.Agents;

// PracticeAgent 专门负责根据课程资料生成练习题、答案和解析。
public sealed class PracticeAgent : IStudySubAgent
{
    private readonly StudyToolRegistry _tools;

    public PracticeAgent(StudyToolRegistry tools)
    {
        _tools = tools;
    }

    public string Name => "PracticeAgent";

    public string Description => "负责根据课程资料生成练习题、答案和解析。";

    // 练习题生成工具由 PracticeAgent 处理。
    public bool CanHandle(string toolName)
    {
        return toolName is "generate_quiz";
    }

    // 调用工具注册表执行 generate_quiz，并把结果返回给 Agent Loop。
    public Task<ToolExecutionResult> ExecuteAsync(
        ToolCallPlan plan,
        CancellationToken cancellationToken)
    {
        return _tools.ExecuteAsync(plan, cancellationToken);
    }
}
