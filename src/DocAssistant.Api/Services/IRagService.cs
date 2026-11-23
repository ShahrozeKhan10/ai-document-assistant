using DocAssistant.Api.DTOs;

namespace DocAssistant.Api.Services;

public interface IRagService
{
    Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken ct);
}
