using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocAssistant.Api.Options;
using Microsoft.Extensions.Options;

namespace DocAssistant.Api.Services.VectorStore;

/// <summary>
/// Stores chunk vectors in Qdrant using its REST API.
/// Each point = one chunk: id = chunk id, vector = embedding, payload = text + where it came from.
/// </summary>
public class QdrantVectorStore : IVectorStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static volatile bool _collectionReady;
    private static readonly SemaphoreSlim EnsureLock = new(1, 1);

    private readonly HttpClient _http;
    private readonly string _collection;
    private readonly int _vectorSize;
    private readonly ILogger<QdrantVectorStore> _logger;

    public QdrantVectorStore(
        HttpClient http,
        IOptions<QdrantOptions> qdrant,
        IOptions<GeminiOptions> gemini,
        ILogger<QdrantVectorStore> logger)
    {
        _http = http;
        _collection = qdrant.Value.Collection;
        _vectorSize = gemini.Value.EmbeddingDimensions; // must match the embedding size
        _logger = logger;
    }

    public async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (_collectionReady) return;

        await EnsureLock.WaitAsync(ct);
        try
        {
            if (_collectionReady) return;

            using var check = await _http.GetAsync($"collections/{_collection}", ct);
            if (check.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Creating Qdrant collection {Collection} ({Size} dims, cosine)", _collection, _vectorSize);

                using (await SendAsync(HttpMethod.Put, $"collections/{_collection}", new
                {
                    vectors = new { size = _vectorSize, distance = "Cosine" }
                }, ct)) { }

                // Index on document_id makes "search only in these documents" fast.
                using (await SendAsync(HttpMethod.Put, $"collections/{_collection}/index", new
                {
                    field_name = "document_id",
                    field_schema = "keyword"
                }, ct)) { }
            }
            else
            {
                check.EnsureSuccessStatusCode();
            }

            _collectionReady = true;
        }
        finally
        {
            EnsureLock.Release();
        }
    }

    public async Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken ct)
    {
        await EnsureCollectionAsync(ct);

        foreach (var batch in points.Chunk(100))
        {
            var body = new
            {
                points = batch.Select(p => new
                {
                    id = p.Id.ToString(),
                    vector = p.Vector,
                    payload = new Dictionary<string, object>
                    {
                        ["document_id"] = p.DocumentId.ToString(),
                        ["chunk_index"] = p.ChunkIndex,
                        ["page"] = p.PageNumber,
                        ["file_name"] = p.FileName,
                        ["text"] = p.Text
                    }
                }).ToList()
            };

            using var _ = await SendAsync(HttpMethod.Put, $"collections/{_collection}/points?wait=true", body, ct);
        }
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        float[] vector, int limit, IReadOnlyCollection<Guid>? documentIds, CancellationToken ct)
    {
        await EnsureCollectionAsync(ct);

        object? filter = documentIds is { Count: > 0 }
            ? new
            {
                must = new[]
                {
                    new { key = "document_id", match = new { any = documentIds.Select(id => id.ToString()).ToArray() } }
                }
            }
            : null;

        var body = new Dictionary<string, object?>
        {
            ["query"] = vector,
            ["limit"] = limit,
            ["with_payload"] = true
        };
        if (filter is not null) body["filter"] = filter;

        using var response = await SendAsync(HttpMethod.Post, $"collections/{_collection}/points/query", body, ct);
        var result = await response.Content.ReadFromJsonAsync<QueryResponse>(Json, ct);

        return (result?.Result?.Points ?? new List<ScoredPoint>())
            .Where(p => p.Payload is not null)
            .Select(p => new SearchHit(
                DocumentId: Guid.TryParse(GetString(p.Payload!, "document_id"), out var docId) ? docId : Guid.Empty,
                ChunkIndex: GetInt(p.Payload!, "chunk_index"),
                PageNumber: GetInt(p.Payload!, "page"),
                FileName: GetString(p.Payload!, "file_name"),
                Text: GetString(p.Payload!, "text"),
                Score: p.Score))
            .ToList();
    }

    public async Task DeleteByDocumentAsync(Guid documentId, CancellationToken ct)
    {
        await EnsureCollectionAsync(ct);

        using var _ = await SendAsync(HttpMethod.Post, $"collections/{_collection}/points/delete?wait=true", new
        {
            filter = new
            {
                must = new[] { new { key = "document_id", match = new { value = documentId.ToString() } } }
            }
        }, ct);
    }

    // ---------- helpers ----------

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, body.GetType())
        };

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();
            throw new HttpRequestException($"Qdrant {method} {path} failed ({(int)response.StatusCode}): {error}");
        }
        return response;
    }

    private static string GetString(Dictionary<string, JsonElement> payload, string key) =>
        payload.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int GetInt(Dictionary<string, JsonElement> payload, string key) =>
        payload.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    private record QueryResponse(QueryResult? Result);
    private record QueryResult(List<ScoredPoint>? Points);
    private record ScoredPoint(JsonElement Id, double Score, Dictionary<string, JsonElement>? Payload);
}
