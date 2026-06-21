using Microsoft.AspNetCore.Http.Features;
using SmartStudyAgent.Agents;
using SmartStudyAgent.Memory;
using SmartStudyAgent.Models;
using SmartStudyAgent.Services;
using SmartStudyAgent.Tools;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

const long MaxUploadBytes = 100 * 1024 * 1024;
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = MaxUploadBytes;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBytes;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddHttpClient<ILlmService, OpenAiCompatibleLlmService>();
builder.Services.AddSingleton<ConversationMemory>();
builder.Services.AddSingleton<LongTermMemoryStore>();
builder.Services.AddSingleton<OcrService>();
builder.Services.AddSingleton<DocumentService>();
builder.Services.AddSingleton<LocalEmbeddingService>();
builder.Services.AddSingleton<VectorRagService>();
builder.Services.AddSingleton<DocumentSearchTool>();
builder.Services.AddSingleton<MaterialSummaryTool>();
builder.Services.AddSingleton<QuizTool>();
builder.Services.AddSingleton<StudyPlanTool>();
builder.Services.AddSingleton<MaterialListTool>();
builder.Services.AddSingleton<LearningInsightTool>();
builder.Services.AddSingleton<StudyToolRegistry>();
builder.Services.AddSingleton<IStudySubAgent, MaterialAgent>();
builder.Services.AddSingleton<IStudySubAgent, PracticeAgent>();
builder.Services.AddSingleton<IStudySubAgent, PlanningAgent>();
builder.Services.AddSingleton<IStudySubAgent, InsightAgent>();
builder.Services.AddSingleton<StudyAgent>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();

app.MapGet("/api/info", () => Results.Ok(new
{
    name = "SmartStudyAgent",
    description = "A .NET 8 AI Agent backend for study assistance.",
    endpoints = new[]
    {
        "GET /api/materials",
        "POST /api/materials",
        "POST /api/materials/upload",
        "POST /api/agent/chat",
        "GET /api/memory/{sessionId}",
        "DELETE /api/memory/{sessionId}"
    }
}));

app.MapGet("/api/materials", async (DocumentService documents, CancellationToken cancellationToken) =>
{
    var materials = await documents.ListMaterialsAsync(cancellationToken);
    return Results.Ok(materials);
});

app.MapGet("/api/ocr/status", (OcrService ocr) =>
{
    return Results.Ok(ocr.GetStatus());
});

app.MapGet("/api/materials/{id}/file", async (
    string id,
    DocumentService documents,
    CancellationToken cancellationToken) =>
{
    var original = await documents.GetOriginalFileAsync(id, cancellationToken);
    return original is null
        ? Results.NotFound(new { error = "Original file was not found. Please upload this material again." })
        : Results.File(
            original.Value.Path,
            original.Value.ContentType,
            original.Value.DownloadName,
            enableRangeProcessing: true);
});

app.MapGet("/api/materials/{id}", async (
    string id,
    DocumentService documents,
    CancellationToken cancellationToken) =>
{
    var preview = await documents.GetMaterialPreviewAsync(id, 3000, cancellationToken);
    return preview is null ? Results.NotFound(new { error = "Material was not found." }) : Results.Ok(preview);
});

app.MapPost("/api/materials", async (
    CreateMaterialRequest request,
    DocumentService documents,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
    {
        return Results.BadRequest(new { error = "Title and content are required." });
    }

    var material = await documents.SaveMaterialAsync(request.Title, request.Content, cancellationToken);
    return Results.Created($"/api/materials/{material.Id}", material);
});

app.MapPost("/api/materials/upload", async (
    HttpRequest request,
    DocumentService documents,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Please upload a file using multipart/form-data." });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "File is required." });
    }

    if (file.Length > MaxUploadBytes)
    {
        return Results.BadRequest(new { error = "File is too large. Please upload a file smaller than 100 MB." });
    }

    var title = form.TryGetValue("title", out var titleValue) && !string.IsNullOrWhiteSpace(titleValue)
        ? titleValue.ToString()
        : Path.GetFileNameWithoutExtension(file.FileName);

    try
    {
        await using var stream = file.OpenReadStream();
        var material = await documents.SaveUploadedMaterialBestEffortAsync(
            title,
            file.FileName,
            stream,
            cancellationToken);

        return Results.Created($"/api/materials/{material.Id}", material);
    }
    catch (NotSupportedException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to upload and parse material {FileName}", file.FileName);
        return Results.BadRequest(new { error = $"文件解析失败：{ex.Message}" });
    }
});

app.MapDelete("/api/materials/{id}", async (
    string id,
    DocumentService documents,
    CancellationToken cancellationToken) =>
{
    var deleted = await documents.DeleteMaterialAsync(id, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound(new { error = "Material was not found." });
});

app.MapPut("/api/materials/{id}", async (
    string id,
    UpdateMaterialRequest request,
    DocumentService documents,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Title))
    {
        return Results.BadRequest(new { error = "Title is required." });
    }

    var material = await documents.RenameMaterialAsync(id, request.Title, cancellationToken);
    return material is null ? Results.NotFound(new { error = "Material was not found." }) : Results.Ok(material);
});

app.MapPost("/api/agent/chat", async (
    AgentChatRequest request,
    StudyAgent agent,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "Message is required." });
    }

    var response = await agent.RunAsync(request, cancellationToken);
    return Results.Ok(response);
});

app.MapPost("/api/agent/chat/stream", async (
    AgentChatRequest request,
    StudyAgent agent,
    HttpResponse response,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        response.StatusCode = StatusCodes.Status400BadRequest;
        await response.WriteAsync("Message is required.", cancellationToken);
        return;
    }

    response.Headers.ContentType = "text/event-stream; charset=utf-8";
    response.Headers.CacheControl = "no-cache";

    var result = await agent.RunAsync(request, cancellationToken);
    await response.WriteAsync(
        $"event: steps\ndata: {JsonSerializer.Serialize(result.Steps, jsonOptions)}\n\n",
        cancellationToken);
    await response.Body.FlushAsync(cancellationToken);

    foreach (var chunk in result.Answer.Chunk(24))
    {
        await response.WriteAsync(
            $"event: answer\ndata: {JsonSerializer.Serialize(new string(chunk), jsonOptions)}\n\n",
            cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
        await Task.Delay(20, cancellationToken);
    }

    await response.WriteAsync("event: done\ndata: [DONE]\n\n", cancellationToken);
});

app.MapGet("/api/memory/{sessionId}", (string sessionId, ConversationMemory memory) =>
{
    return Results.Ok(memory.GetMessages(sessionId));
});

app.MapGet("/api/sessions", (ConversationMemory memory) =>
{
    return Results.Ok(memory.ListSessions());
});

app.MapDelete("/api/memory/{sessionId}", (string sessionId, ConversationMemory memory) =>
{
    memory.Clear(sessionId);
    return Results.NoContent();
});

app.MapGet("/api/long-term-memory/{sessionId}", (string sessionId, LongTermMemoryStore memory) =>
{
    return Results.Ok(memory.Get(sessionId));
});

app.MapPut("/api/long-term-memory/{sessionId}", (
    string sessionId,
    UpdateLongTermMemoryRequest request,
    LongTermMemoryStore memory) =>
{
    return Results.Ok(memory.Update(sessionId, request.LearningGoal, request.Preference));
});

app.Run();
