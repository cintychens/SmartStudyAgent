using SmartStudyAgent.Memory;
using SmartStudyAgent.Models;
using SmartStudyAgent.Services;
using SmartStudyAgent.Tools;

namespace SmartStudyAgent.Agents;

public sealed class StudyAgent
{
    private readonly StudyToolRegistry _tools;
    private readonly IReadOnlyList<IStudySubAgent> _subAgents;
    private readonly ConversationMemory _memory;
    private readonly LongTermMemoryStore _longTermMemory;
    private readonly DocumentService _documents;
    private readonly ILlmService _llm;
    private readonly ILogger<StudyAgent> _logger;

    public StudyAgent(
        StudyToolRegistry tools,
        IEnumerable<IStudySubAgent> subAgents,
        ConversationMemory memory,
        LongTermMemoryStore longTermMemory,
        DocumentService documents,
        ILlmService llm,
        ILogger<StudyAgent> logger)
    {
        _tools = tools;
        _subAgents = subAgents.ToList();
        _memory = memory;
        _longTermMemory = longTermMemory;
        _documents = documents;
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentChatResponse> RunAsync(
        AgentChatRequest request,
        CancellationToken cancellationToken)
    {
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Guid.NewGuid().ToString("N")
            : request.SessionId;

        _memory.Add(sessionId, "user", request.Message);

        var steps = new List<AgentStep>();
        var maxSteps = Math.Clamp(request.MaxSteps, 1, 8);
        var observations = new List<string>();

        for (var step = 1; step <= maxSteps; step++)
        {
            var plan = PlanNextAction(request.Message, observations);
            AddSelectedMaterials(plan, request.MaterialIds);
            if (plan.ToolName.Equals("finish", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var subAgent = SelectSubAgent(plan.ToolName);
            _logger.LogInformation(
                "CoordinatorAgent step {Step}: delegate {Tool} to {SubAgent}",
                step,
                plan.ToolName,
                subAgent.Name);

            var result = await subAgent.ExecuteAsync(plan, cancellationToken);
            observations.Add(result.Observation);

            steps.Add(new AgentStep(
                step,
                $"CoordinatorAgent -> {subAgent.Name}: {plan.Thought}",
                result.ToolName,
                result.Observation,
                subAgent.Name));

            if (ShouldStopAfterTool(plan.ToolName))
            {
                break;
            }
        }

        var answer = await BuildFinalAnswerAsync(
            sessionId,
            request.Message,
            steps,
            cancellationToken);
        answer = await AddMaterialSourceHeaderAsync(answer, request.MaterialIds, cancellationToken);

        _memory.Add(sessionId, "assistant", answer);

        return new AgentChatResponse(
            sessionId,
            answer,
            steps,
            _memory.GetMessages(sessionId));
    }

    private IStudySubAgent SelectSubAgent(string toolName)
    {
        var subAgent = _subAgents.FirstOrDefault(agent => agent.CanHandle(toolName));
        if (subAgent is not null)
        {
            return subAgent;
        }

        throw new InvalidOperationException($"No sub-agent can handle tool '{toolName}'.");
    }

    private static ToolCallPlan PlanNextAction(string userMessage, IReadOnlyList<string> observations)
    {
        if (observations.Count > 0)
        {
            return new ToolCallPlan("已有工具观察结果，可以生成最终回答。", "finish", new Dictionary<string, string>());
        }

        var message = userMessage.ToLowerInvariant();

        if (ContainsAny(message, "有哪些资料", "列出资料", "资料列表", "查看资料", "已有资料", "list materials"))
        {
            return new ToolCallPlan(
                "用户需要查看已有课程资料，先调用资料列表工具。",
                "list_materials",
                new Dictionary<string, string>());
        }

        if (ContainsAny(message, "关键词", "知识点", "提纲", "复习提纲", "易错点", "知识树", "重点整理"))
        {
            return new ToolCallPlan(
                "用户需要学习辅助整理，先调用学习重点提取工具。",
                "extract_learning_points",
                new Dictionary<string, string> { ["goal"] = userMessage });
        }

        if (IsQuizRequest(message))
        {
            return new ToolCallPlan(
                "用户需要围绕资料生成问题、答案或练习题，先调用练习题 Agent。",
                "generate_quiz",
                new Dictionary<string, string> { ["query"] = userMessage });
        }

        if (ContainsAny(message, "总结", "概括", "摘要", "重点", "文档", "课件", "资料", "说了什么", "主要内容", "讲了什么"))
        {
            return new ToolCallPlan(
                "用户需要理解资料核心内容，先调用总结工具。",
                "summarize_material",
                new Dictionary<string, string> { ["target"] = userMessage });
        }

        if (IsQuizRequest(message))
        {
            return new ToolCallPlan(
                "用户需要练习巩固，先从课程资料生成题目。",
                "generate_quiz",
                new Dictionary<string, string> { ["query"] = userMessage });
        }

        if (ContainsAny(message, "计划", "规划", "安排", "三天", "七天", "7天", "学习路径", "study plan"))
        {
            return new ToolCallPlan(
                "用户需要学习安排，先调用学习计划工具。",
                "create_study_plan",
                new Dictionary<string, string> { ["goal"] = userMessage });
        }

        return new ToolCallPlan(
            "用户在提问课程内容，先检索本地课程资料。",
            "search_materials",
            new Dictionary<string, string> { ["query"] = userMessage });
    }

    private async Task<string> BuildFinalAnswerAsync(
        string sessionId,
        string userMessage,
        IReadOnlyList<AgentStep> steps,
        CancellationToken cancellationToken)
    {
        if (steps.Count == 1
            && steps[0].Action is "summarize_material" or "search_materials" or "generate_quiz")
        {
            return steps[0].Observation;
        }

        var toolContext = steps.Count == 0
            ? "No tool was called."
            : string.Join(Environment.NewLine, steps.Select(s =>
                $"Agent: {s.Agent}\nThought: {s.Thought}\nAction: {s.Action}\nObservation: {s.Observation}"));

        var memoryContext = _memory.BuildContext(sessionId);
        var longTermMemory = _longTermMemory.Get(sessionId);
        var toolList = string.Join(Environment.NewLine, _tools.ListTools().Select(t => $"- {t.Name}: {t.Description}"));

        var systemPrompt = """
                           You are SmartStudyAgent, a .NET learning assistant.
                           Answer in Chinese.
                           Use the current tool observations as the factual source.
                           Conversation memory is only for dialogue continuity, not for replacing the current material content.
                           If memory conflicts with tool observations, ignore memory.
                           Do not mention topics that are not present in the current tool observations.
                           Be concrete, concise, and helpful for a student.
                           """;

        var userPrompt = $"""
                          Available tools:
                          {toolList}

                          Conversation memory:
                          {memoryContext}

                          Long-term memory:
                          Learning goal: {longTermMemory.LearningGoal ?? "Not set"}
                          Preference: {longTermMemory.Preference ?? "Not set"}

                          User request:
                          {userMessage}

                          Agent loop trace:
                          {toolContext}

                          Please produce the final answer for the user.
                          """;

        return await _llm.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
    }

    private static bool ShouldStopAfterTool(string toolName)
    {
        return toolName is "search_materials"
            or "summarize_material"
            or "generate_quiz"
            or "create_study_plan"
            or "list_materials"
            or "extract_learning_points";
    }

    private static bool ContainsAny(string source, params string[] terms)
    {
        return terms.Any(term => source.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsQuizRequest(string message)
    {
        return ContainsAny(
            message,
            "\u9898",
            "\u95ee\u9898",
            "\u7b54\u6848",
            "\u7ec3\u4e60",
            "\u6d4b\u8bd5",
            "\u9009\u62e9\u9898",
            "\u5224\u65ad\u9898",
            "\u7b80\u7b54\u9898",
            "\u51fa\u51e0\u9053",
            "\u51fa\u4e00\u4e9b",
            "\u8003\u6211",
            "quiz",
            "exam");
    }

    private static void AddSelectedMaterials(ToolCallPlan plan, IReadOnlyList<string>? materialIds)
    {
        if (materialIds is null || materialIds.Count == 0 || plan.ToolName.Equals("finish", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        plan.Arguments["materialIds"] = string.Join(
            ',',
            materialIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<string> AddMaterialSourceHeaderAsync(
        string answer,
        IReadOnlyList<string>? materialIds,
        CancellationToken cancellationToken)
    {
        if (materialIds is null || materialIds.Count == 0)
        {
            return answer;
        }

        var ids = materialIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
        {
            return answer;
        }

        var materials = await _documents.GetMaterialContentsByIdsAsync(ids, cancellationToken);
        var orderedTitles = ids
            .Select(id => materials.FirstOrDefault(material => material.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(material => material is not null)
            .Select((material, index) => $"{index + 1}. {material!.Title}")
            .ToList();

        if (orderedTitles.Count == 0)
        {
            return answer;
        }

        var header = "本次回答基于以下资料："
            + Environment.NewLine
            + string.Join(Environment.NewLine, orderedTitles);

        return $"{header}{Environment.NewLine}{Environment.NewLine}{answer}";
    }
}
