using DocAssistant.Api.DTOs;

namespace DocAssistant.Api.Services;

public interface IRagService
{
    Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken ct);

    /// <summary>Same as AskAsync, but yields the answer piece by piece, then the cited sources.</summary>
    IAsyncEnumerable<ChatStreamEvent> AskStreamAsync(ChatRequest request, CancellationToken ct);
}
