using DocAssistant.Api.DTOs;

namespace DocAssistant.Api.Services;

/// <summary>Every method takes the logged-in user's id, so users only ever see their own documents.</summary>
public interface IDocumentService
{
    Task<DocumentDetailDto> UploadAsync(IFormFile file, Guid ownerId, CancellationToken ct);
    Task<IReadOnlyList<DocumentSummaryDto>> ListAsync(Guid ownerId, CancellationToken ct);
    Task<DocumentDetailDto?> GetAsync(Guid id, Guid ownerId, CancellationToken ct);
    Task<IReadOnlyList<ChunkDto>?> GetChunksAsync(Guid id, Guid ownerId, CancellationToken ct);

    /// <summary>Re-creates the embeddings for an existing document (e.g. ones uploaded before Phase 2).</summary>
    Task<DocumentDetailDto?> ReindexAsync(Guid id, Guid ownerId, CancellationToken ct);

    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken ct);
}

/// <summary>The one thing RagService needs from documents: which ones this user may search.</summary>
public interface IDocumentScope
{
    Task<IReadOnlyList<Guid>> GetReadyDocumentIdsAsync(Guid ownerId, CancellationToken ct);
}
