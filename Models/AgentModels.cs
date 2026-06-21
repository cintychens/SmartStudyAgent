namespace SmartStudyAgent.Models;

public sealed record AgentChatRequest(
    string Message,
    string? SessionId = null,
    int MaxSteps = 5,
    IReadOnlyList<string>? MaterialIds = null);

public sealed record AgentChatResponse(
    string SessionId,
    string Answer,
    IReadOnlyList<AgentStep> Steps,
    IReadOnlyList<ChatMessageRecord> Memory);

public sealed record AgentStep(
    int Step,
    string Thought,
    string Action,
    string Observation,
    string Agent = "CoordinatorAgent");

public sealed record ChatMessageRecord(
    string Role,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record SessionInfo(
    string SessionId,
    int MessageCount,
    DateTimeOffset UpdatedAt,
    string Preview);

public sealed record LongTermMemoryRecord(
    string SessionId,
    string? LearningGoal,
    string? Preference,
    DateTimeOffset UpdatedAt);

public sealed record UpdateLongTermMemoryRequest(
    string? LearningGoal,
    string? Preference);

public sealed record CreateMaterialRequest(
    string Title,
    string Content);

public sealed record UpdateMaterialRequest(
    string Title);

public sealed record CourseMaterial(
    string Id,
    string Title,
    string FileName,
    string FileType,
    int CharacterCount,
    long FileSize,
    DateTimeOffset CreatedAt);

public sealed record MaterialPreview(
    string Id,
    string Title,
    string FileType,
    int CharacterCount,
    long FileSize,
    DateTimeOffset CreatedAt,
    string Preview);

public sealed record MaterialContent(
    string Id,
    string Title,
    string Content);

public sealed record SearchResult(
    string MaterialId,
    string Title,
    string Snippet,
    int Score);

public sealed record RagChunk(
    string MaterialId,
    string Title,
    string Text,
    double Score);

public sealed record ToolExecutionResult(
    string ToolName,
    string Observation);

public sealed record ToolCallPlan(
    string Thought,
    string ToolName,
    Dictionary<string, string> Arguments);
