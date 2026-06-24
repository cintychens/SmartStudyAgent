using Microsoft.AspNetCore.Http.Features;
using SmartStudyAgent.Agents;
using SmartStudyAgent.Memory;
using SmartStudyAgent.Models;
using SmartStudyAgent.Services;
using SmartStudyAgent.Tools;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// 设置课程资料上传大小上限，Kestrel 和 multipart 表单解析都会使用这个限制。
const long MaxUploadBytes = 100 * 1024 * 1024;
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// 配置 Web 服务器允许上传较大的 PDF/PPTX 课程资料。
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = MaxUploadBytes;
});

// 配置 ASP.NET Core 表单解析，保证 multipart 文件上传不会被默认大小限制拦截。
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBytes;
});

// 开启本地开发时的跨域访问，方便静态前端页面调用后端 API。
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// 注册核心服务：LLM 调用、短期/长期记忆、资料存储、OCR 和轻量本地 RAG。
builder.Services.AddHttpClient<ILlmService, OpenAiCompatibleLlmService>();
builder.Services.AddSingleton<ConversationMemory>();
builder.Services.AddSingleton<LongTermMemoryStore>();
builder.Services.AddSingleton<OcrService>();
builder.Services.AddSingleton<DocumentService>();
builder.Services.AddSingleton<LocalEmbeddingService>();
builder.Services.AddSingleton<VectorRagService>();

// 注册 Agent Loop 中可以调用的自定义工具。
builder.Services.AddSingleton<DocumentSearchTool>();
builder.Services.AddSingleton<MaterialSummaryTool>();
builder.Services.AddSingleton<QuizTool>();
builder.Services.AddSingleton<StudyPlanTool>();
builder.Services.AddSingleton<MaterialListTool>();
builder.Services.AddSingleton<LearningInsightTool>();
builder.Services.AddSingleton<StudyToolRegistry>();

// 注册多个专门子 Agent，CoordinatorAgent 会把不同工具任务委托给它们执行。
builder.Services.AddSingleton<IStudySubAgent, MaterialAgent>();
builder.Services.AddSingleton<IStudySubAgent, PracticeAgent>();
builder.Services.AddSingleton<IStudySubAgent, PlanningAgent>();
builder.Services.AddSingleton<IStudySubAgent, InsightAgent>();
builder.Services.AddSingleton<StudyAgent>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();

// 基础信息接口，用于前端确认后端服务和可用端点。
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

// 返回已上传的课程资料列表，供资料管理模块展示。
app.MapGet("/api/materials", async (DocumentService documents, CancellationToken cancellationToken) =>
{
    var materials = await documents.ListMaterialsAsync(cancellationToken);
    return Results.Ok(materials);
});

// 返回 OCR 环境检测结果，用于判断 Tesseract、Poppler 等工具是否可用。
app.MapGet("/api/ocr/status", (OcrService ocr) =>
{
    return Results.Ok(ocr.GetStatus());
});

// 返回原始上传文件，前端可用它预览 PDF 或下载 PPTX/TXT/MD 原文件。
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

// 返回单份资料的文本预览和元数据。
app.MapGet("/api/materials/{id}", async (
    string id,
    DocumentService documents,
    CancellationToken cancellationToken) =>
{
    var preview = await documents.GetMaterialPreviewAsync(id, 3000, cancellationToken);
    return preview is null ? Results.NotFound(new { error = "Material was not found." }) : Results.Ok(preview);
});

// 添加手动文本资料，这个接口独立于文件上传功能。
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

// 上传一个或多个文件；单文件保持旧响应格式，多文件返回批量结果。
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
    var files = form.Files.GetFiles("file").Where(file => file.Length > 0).ToList();
    if (files.Count == 0)
    {
        return Results.BadRequest(new { error = "File is required." });
    }

    if (files.Any(file => file.Length > MaxUploadBytes))
    {
        return Results.BadRequest(new { error = "File is too large. Please upload a file smaller than 100 MB." });
    }

    var sharedTitle = form.TryGetValue("title", out var titleValue) && !string.IsNullOrWhiteSpace(titleValue)
        ? titleValue.ToString()
        : string.Empty;
    var uploaded = new List<CourseMaterial>();
    var failed = new List<object>();

    // 每个文件独立保存，避免某个文件解析失败导致整批上传全部失败。
    foreach (var file in files)
    {
        var title = files.Count == 1 && !string.IsNullOrWhiteSpace(sharedTitle)
            ? sharedTitle
            : Path.GetFileNameWithoutExtension(file.FileName);

        try
        {
            await using var stream = file.OpenReadStream();
            var material = await documents.SaveUploadedMaterialBestEffortAsync(
                title,
                file.FileName,
                stream,
                cancellationToken);

            uploaded.Add(material);
        }
        catch (NotSupportedException ex)
        {
            failed.Add(new { fileName = file.FileName, error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            failed.Add(new { fileName = file.FileName, error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload and parse material {FileName}", file.FileName);
            failed.Add(new { fileName = file.FileName, error = $"文件解析失败：{ex.Message}" });
            continue;
        }
    }

    // 单文件上传继续返回旧格式，保证已有前端代码兼容。
    if (files.Count == 1)
    {
        return uploaded.Count == 1
            ? Results.Created($"/api/materials/{uploaded[0].Id}", uploaded[0])
            : Results.BadRequest(failed[0]);
    }

    if (uploaded.Count == 0)
    {
        return Results.BadRequest(new { materials = uploaded, failed });
    }

    return Results.Ok(new { materials = uploaded, failed });
});

// 删除课程资料，同时删除对应保存的原始文件。
app.MapDelete("/api/materials/{id}", async (
    string id,
    DocumentService documents,
    CancellationToken cancellationToken) =>
{
    var deleted = await documents.DeleteMaterialAsync(id, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound(new { error = "Material was not found." });
});

// 仅修改资料标题，不改变已经提取的正文和原始上传文件。
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

// 普通非流式 Agent 问答接口，直接返回完整回答。
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

// 流式 Agent 问答接口：先发送推理步骤，再按小片段发送最终回答。
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

// 短期记忆接口，用于会话切换、历史消息读取和清空当前会话记忆。
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

// 长期记忆接口，用于保存学习目标和偏好，不影响普通对话 Memory。
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
