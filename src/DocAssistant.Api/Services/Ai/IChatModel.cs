namespace DocAssistant.Api.Services.Ai;

/// <summary>A text-generation model (Gemini today; easy to swap for OpenAI later).</summary>
public interface IChatModel
{
    /// <summary>Returns the whole answer at once.</summary>
    Task<string> GenerateAsync(string systemInstruction, string prompt, CancellationToken ct);

    /// <summary>Returns the answer piece by piece as the model writes it.</summary>
    IAsyncEnumerable<string> StreamAsync(string systemInstruction, string prompt, CancellationToken ct);
}
