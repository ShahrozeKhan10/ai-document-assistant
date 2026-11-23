namespace DocAssistant.Api.Services.VectorStore;

public record VectorPoint(
    Guid Id,
    float[] Vector,
    Guid DocumentId,
    int ChunkIndex,
    int PageNumber,
    string FileName,
    string Text);

public record SearchHit(
    Guid DocumentId,
    int ChunkIndex,
    int PageNumber,
    string FileName,
    string Text,
    double Score);

public interface IVectorStore
{
    Task EnsureCollectionAsync(CancellationToken ct);
    Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken ct);

    /// <summary>Finds the chunks closest to the vector, optionally only inside some documents.</summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(
        float[] vector, int limit, IReadOnlyCollection<Guid>? documentIds, CancellationToken ct);

    Task DeleteByDocumentAsync(Guid documentId, CancellationToken ct);
}
