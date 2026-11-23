using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocAssistant.Api.Options;
using Microsoft.Extensions.Options;

namespace DocAssistant.Api.Services.Ai;

public class GeminiException : Exception
{
    public GeminiException(string message) : base(message) { }
}

/// <summary>
/// Talks to the Gemini REST API with HttpClient (no SDK), so every request is visible and easy to explain.
/// Implements both embeddings and text generation.
/// </summary>
public class GeminiClient : IEmbeddingService, IChatModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaxAttempts = 4;

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiClient> _logger;

    public GeminiClient(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    // ---------- Embeddings ----------

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var results = new List<float[]>(texts.Count);
        var batchSize = Math.Clamp(_options.EmbeddingBatchSize, 1, 100);

        for (var i = 0; i < texts.Count; i += batchSize)
        {
            var batch = texts.Skip(i).Take(batchSize).ToList();
            var body = new
            {
                requests = batch.Select(text => new
                {
                    model = $"models/{_options.EmbeddingModel}",
                    content = new { parts = new[] { new { text } } },
                    taskType = "RETRIEVAL_DOCUMENT",
                    outputDimensionality = _options.EmbeddingDimensions
                }).ToList()
            };

            var response = await PostAsync<BatchEmbedResponse>(
                $"models/{_options.EmbeddingModel}:batchEmbedContents", body, ct);

            if (response.Embeddings is null || response.Embeddings.Count != batch.Count)
                throw new GeminiException("Gemini returned a different number of embeddings than requested.");

            results.AddRange(response.Embeddings.Select(e => e.Values ?? Array.Empty<float>()));
        }

        return results;
    }

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken ct)
    {
        var body = new
        {
            model = $"models/{_options.EmbeddingModel}",
            content = new { parts = new[] { new { text } } },
            taskType = "RETRIEVAL_QUERY",
            outputDimensionality = _options.EmbeddingDimensions
        };

        var response = await PostAsync<EmbedResponse>($"models/{_options.EmbeddingModel}:embedContent", body, ct);
        return response.Embedding?.Values ?? throw new GeminiException("Gemini returned no embedding.");
    }

    // ---------- Text generation ----------

    public async Task<string> GenerateAsync(string systemInstruction, string prompt, CancellationToken ct)
    {
        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = prompt } } }
            }
        };

        var response = await PostAsync<GenerateResponse>($"models/{_options.ChatModel}:generateContent", body, ct);

        var parts = response.Candidates?.FirstOrDefault()?.Content?.Parts;
        if (parts is null || parts.Count == 0)
            throw new GeminiException("Gemini returned an empty answer (it may have been blocked by safety filters).");

        // Skip "thought" parts some models return; keep only the visible answer text.
        return string.Concat(parts.Where(p => p.Thought != true).Select(p => p.Text)).Trim();
    }

    // ---------- HTTP with retry ----------

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new GeminiException("Gemini API key is missing. Run set-gemini-key.bat in the project folder.");

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body, body.GetType(), options: Json)
            };
            request.Headers.Add("x-goog-api-key", _options.ApiKey);

            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<T>(Json, ct);
                return result ?? throw new GeminiException("Gemini returned an empty response.");
            }

            var error = await response.Content.ReadAsStringAsync(ct);
            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;

            if (retryable && attempt < MaxAttempts)
            {
                // Free tier has rate limits: wait 2s, 4s, 8s before trying again.
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning("Gemini returned {Status}; retrying in {Delay}s (attempt {Attempt})",
                    (int)response.StatusCode, delay.TotalSeconds, attempt);
                await Task.Delay(delay, ct);
                continue;
            }

            throw new GeminiException($"Gemini API error {(int)response.StatusCode}: {Shorten(error)}");
        }
    }

    private static string Shorten(string text) => text.Length <= 500 ? text : text[..500] + "...";

    // ---------- Response shapes ----------

    private record EmbedResponse(ContentEmbedding? Embedding);
    private record BatchEmbedResponse(List<ContentEmbedding>? Embeddings);
    private record ContentEmbedding(float[]? Values);

    private record GenerateResponse(List<Candidate>? Candidates);
    private record Candidate(GeneratedContent? Content, string? FinishReason);
    private record GeneratedContent(List<GeneratedPart>? Parts);
    private record GeneratedPart(string? Text, bool? Thought);
}
