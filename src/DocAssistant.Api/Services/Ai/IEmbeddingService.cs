namespace DocAssistant.Api.Services.Ai;

/// <summary>Turns text into vectors (lists of numbers) so similar meaning = nearby vectors.</summary>
public interface IEmbeddingService
{
    /// <summary>Embeds document chunks (task type RETRIEVAL_DOCUMENT).</summary>
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct);

    /// <summary>Embeds a user's question (task type RETRIEVAL_QUERY).</summary>
    Task<float[]> EmbedQueryAsync(string text, CancellationToken ct);
}
