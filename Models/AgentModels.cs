namespace SmartStudyAgent.Models;

// AgentChatRequest 是前端向后端发起一次学习问答时传入的请求模型。
public sealed record AgentChatRequest(
    string Message,
    string? SessionId = null,
    int MaxSteps = 5,
    IReadOnlyList<string>? MaterialIds = null);

// AgentChatResponse 是后端返回给前端的完整问答结果，包含答案、推理步骤和当前记忆。
public sealed record AgentChatResponse(
    string SessionId,
    string Answer,
    IReadOnlyList<AgentStep> Steps,
    IReadOnlyList<ChatMessageRecord> Memory);

// AgentStep 记录一次 Agent Loop 中的 Thought、Action、Observation，以及实际执行的子 Agent。
public sealed record AgentStep(
    int Step,
    string Thought,
    string Action,
    string Observation,
    string Agent = "CoordinatorAgent");

// ChatMessageRecord 表示一条短期会话记忆，Role 用来区分 user 和 assistant。
public sealed record ChatMessageRecord(
    string Role,
    string Content,
    DateTimeOffset CreatedAt);

// SessionInfo 用于前端多会话列表展示，保存会话消息数、更新时间和预览文本。
public sealed record SessionInfo(
    string SessionId,
    int MessageCount,
    DateTimeOffset UpdatedAt,
    string Preview);

// LongTermMemoryRecord 保存用户的长期学习目标和偏好，独立于普通聊天记录。
public sealed record LongTermMemoryRecord(
    string SessionId,
    string? LearningGoal,
    string? Preference,
    DateTimeOffset UpdatedAt);

// UpdateLongTermMemoryRequest 是更新长期记忆时使用的请求模型。
public sealed record UpdateLongTermMemoryRequest(
    string? LearningGoal,
    string? Preference);

// CreateMaterialRequest 用于手动添加纯文本课程资料。
public sealed record CreateMaterialRequest(
    string Title,
    string Content);

// UpdateMaterialRequest 用于修改课程资料标题。
public sealed record UpdateMaterialRequest(
    string Title);

// CourseMaterial 是资料列表页展示用的摘要信息，不直接返回完整正文。
public sealed record CourseMaterial(
    string Id,
    string Title,
    string FileName,
    string FileType,
    int CharacterCount,
    long FileSize,
    DateTimeOffset CreatedAt);

// MaterialPreview 返回资料预览内容和基本元数据，用于前端弹窗预览。
public sealed record MaterialPreview(
    string Id,
    string Title,
    string FileType,
    int CharacterCount,
    long FileSize,
    DateTimeOffset CreatedAt,
    string Preview);

// MaterialContent 是 Agent 工具实际读取资料正文时使用的内部模型。
public sealed record MaterialContent(
    string Id,
    string Title,
    string Content);

// SearchResult 表示一次资料检索命中的结果片段和匹配分数。
public sealed record SearchResult(
    string MaterialId,
    string Title,
    string Snippet,
    int Score);

// RagChunk 表示轻量 RAG 检索返回的文档片段及其相关性分数。
public sealed record RagChunk(
    string MaterialId,
    string Title,
    string Text,
    double Score);

// ToolExecutionResult 是每个自定义工具执行后返回给 Agent Loop 的观察结果。
public sealed record ToolExecutionResult(
    string ToolName,
    string Observation);

// ToolCallPlan 是 CoordinatorAgent 对下一步工具调用做出的计划。
public sealed record ToolCallPlan(
    string Thought,
    string ToolName,
    Dictionary<string, string> Arguments);
