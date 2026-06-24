using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartStudyAgent.Services;

// OpenAiCompatibleLlmService 负责调用 OpenAI 兼容格式的大模型 API，也提供 Mock 兜底。
public sealed class OpenAiCompatibleLlmService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAiCompatibleLlmService> _logger;

    public OpenAiCompatibleLlmService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OpenAiCompatibleLlmService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        // Provider 为 Mock 时不请求外部 API，方便没有 Key 时演示 Agent 流程。
        var provider = _configuration["Llm:Provider"] ?? "Mock";
        if (provider.Equals("Mock", StringComparison.OrdinalIgnoreCase))
        {
            return BuildMockResponse(userPrompt);
        }

        // 从配置中读取模型名称、API Key 和 OpenAI 兼容接口地址。
        var endpoint = BuildEndpoint();
        var model = _configuration["Llm:Model"];
        var apiKey = _configuration["Llm:ApiKey"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            _logger.LogWarning("LLM endpoint or model is missing. Falling back to mock response.");
            return BuildMockResponse(userPrompt);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        // 使用 OpenAI chat/completions 兼容的 messages 格式。
        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.2
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        try
        {
            // 调用真实模型接口，失败时回退到 Mock，保证系统仍可演示。
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LLM request failed with status {StatusCode}: {Body}", response.StatusCode, body);
                return BuildMockResponse(userPrompt);
            }

            using var document = JsonDocument.Parse(body);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content)
                ? BuildMockResponse(userPrompt)
                : content.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM request failed. Falling back to mock response.");
            return BuildMockResponse(userPrompt);
        }
    }

    private string? BuildEndpoint()
    {
        // 优先使用完整 Endpoint；否则用 BaseUrl 拼出 /chat/completions。
        var endpoint = _configuration["Llm:Endpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint;
        }

        var baseUrl = _configuration["Llm:BaseUrl"];
        return string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : $"{baseUrl.TrimEnd('/')}/chat/completions";
    }

    private static string BuildMockResponse(string prompt)
    {
        // Mock 模式根据提示词类型返回演示回答，便于没有真实模型时测试流程。
        if (prompt.Contains("Please summarize this course material", StringComparison.OrdinalIgnoreCase))
        {
            var material = ExtractAfter(prompt, "Please summarize this course material in Chinese:");
            var clipped = Clip(material, 1200);

            return $"""
                   资料内容概要：
                   {clipped}

                   Mock LLM 提示：现在还没有配置真实模型，所以这里展示的是从资料中截取的核心文本。配置真实 API 后，系统会生成更自然的总结、重点和学习建议。
                   """;
        }

        if (prompt.Contains("请根据下面资料生成恰好", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("## 练习题", StringComparison.OrdinalIgnoreCase))
        {
            var count = ExtractRequestedQuizCount(prompt);
            var builder = new StringBuilder();
            builder.AppendLine("## 练习题");
            for (var index = 1; index <= count; index++)
            {
                builder.AppendLine($"{index}. 【选择题】这份资料中的第 {index} 个重点适合如何复习？");
                builder.AppendLine("   A. 只记文件名");
                builder.AppendLine("   B. 结合资料正文理解概念和代码");
                builder.AppendLine("   C. 跳过示例");
                builder.AppendLine("   D. 不做练习");
                builder.AppendLine("   答案：B");
                builder.AppendLine("   解析：Mock 模式只能生成占位题；配置真实模型后会根据资料正文生成更具体的题目。");
                builder.AppendLine();
            }

            return builder.ToString().Trim();
        }

        if (prompt.Contains("Create a 3-day study plan", StringComparison.OrdinalIgnoreCase))
        {
            return """
                   三天学习计划：
                   第一天：通读上传资料，整理主题、关键词和不懂的问题。
                   第二天：围绕重点内容提问，让 Agent 总结难点并生成练习题。
                   第三天：根据练习题查漏补缺，准备答辩时解释 Agent Loop、工具调用和记忆机制。
                   """;
        }

        if (prompt.Contains("Agent loop trace:", StringComparison.OrdinalIgnoreCase))
        {
            var observation = ExtractAfter(prompt, "Observation:");
            observation = observation.Split("Please produce the final answer", StringSplitOptions.None)[0].Trim();

            if (!string.IsNullOrWhiteSpace(observation) && !observation.Contains("No relevant course material", StringComparison.OrdinalIgnoreCase))
            {
                return $"""
                       当前处于 Mock LLM 模式，真实模型 API 尚未配置。

                       根据工具返回结果，本次回答参考了以下资料内容：
                       {Clip(observation, 1500)}

                       你可以在右侧 ReAct Trace 中看到 Thought、Action、Observation，说明 Agent 已经完成了工具调用流程。
                       """;
            }

            return """
                   当前处于 Mock LLM 模式，真实模型 API 尚未配置。

                   本次请求已经经过 Thought -> Action -> Observation 流程处理。右侧 steps 字段可以证明 Agent 会先思考，再选择工具，最后读取工具结果。
                   """;
        }

        return "当前处于 Mock LLM 模式。后端 Agent Loop、工具调用和记忆机制已经工作，但还没有配置真实模型 API Key。";
    }

    private static string ExtractAfter(string value, string marker)
    {
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? value : value[(index + marker.Length)..].Trim();
    }

    private static string Clip(string value, int maxLength)
    {
        value = value.ReplaceLineEndings(Environment.NewLine).Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static int ExtractRequestedQuizCount(string prompt)
    {
        // Mock 出题也会尽量遵守用户请求的题目数量。
        var match = Regex.Match(prompt, @"生成恰好\s*(?<count>\d{1,2})\s*道");
        return match.Success && int.TryParse(match.Groups["count"].Value, out var count)
            ? Math.Clamp(count, 1, 30)
            : 5;
    }
}
