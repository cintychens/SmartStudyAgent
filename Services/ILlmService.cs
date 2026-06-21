namespace SmartStudyAgent.Services;

public interface ILlmService
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}
