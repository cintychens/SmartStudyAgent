namespace SmartStudyAgent.Services;

// ILlmService 抽象大语言模型调用，方便后续替换 OpenAI、VectorEngine 或本地模型。
public interface ILlmService
{
    // 根据系统提示词和用户提示词生成回答。
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}
