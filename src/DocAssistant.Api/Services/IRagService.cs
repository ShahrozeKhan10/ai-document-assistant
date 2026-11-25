using DocAssistant.Api.DTOs;

namespace DocAssistant.Api.Services;

public interface IRagService
{
    /// <summary>Answers a question using only documents owned by <paramref name="ownerId"/>.</summary>
    Task<ChatResponse> AskAsync(ChatRequest request, Guid ownerId, CancellationToken ct);

    /// <summary>Same as AskAsync, but yields the answer piece by piece, then the cited sources.</summary>
    IAsyncEnumerable<ChatStreamEvent> AskStreamAsync(ChatRequest request, Guid ownerId, CancellationToken ct);
}
