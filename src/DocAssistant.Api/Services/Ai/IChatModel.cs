namespace DocAssistant.Api.Services.Ai;

/// <summary>A text-generation model (Gemini today; easy to swap for OpenAI later).</summary>
public interface IChatModel
{
    Task<string> GenerateAsync(string systemInstruction, string prompt, CancellationToken ct);
}
