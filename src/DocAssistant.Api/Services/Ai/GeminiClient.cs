using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
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
/// Implements embeddings, text generation and streaming generation.
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
        var response = await PostAsync<GenerateResponse>(
            $"models/{_options.ChatModel}:generateContent", BuildGenerateBody(systemInstruction, prompt), ct);

        var text = ExtractText(response);
        if (string.IsNullOrWhiteSpace(text))
            throw new GeminiException("Gemini returned an empty answer (it may have been blocked by safety filters).");

        return text.Trim();
    }

    /// <summary>
    /// streamGenerateContent with alt=sse sends Server-Sent Events: one "data: {json}" line per piece of the answer.
    /// We read the response line by line and yield each piece as soon as it arrives.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        string systemInstruction, string prompt, [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await SendWithRetryAsync(
            $"models/{_options.ChatModel}:streamGenerateContent?alt=sse",
            BuildGenerateBody(systemInstruction, prompt),
            HttpCompletionOption.ResponseHeadersRead, // don't wait for the whole body
            ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var json = line["data:".Length..].Trim();
            if (json.Length == 0) continue;

            var piece = ExtractText(JsonSerializer.Deserialize<GenerateResponse>(json, Json));
            if (!string.IsNullOrEmpty(piece)) yield return piece;
        }
    }

    private static object BuildGenerateBody(string systemInstruction, string prompt) => new
    {
        systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
        contents = new[]
        {
            new { role = "user", parts = new[] { new { text = prompt } } }
        }
    };

    /// <summary>Joins the visible text parts, skipping "thought" parts some models return.</summary>
    private static string ExtractText(GenerateResponse? response)
    {
        var parts = response?.Candidates?.FirstOrDefault()?.Content?.Parts;
        return parts is null ? "" : string.Concat(parts.Where(p => p.Thought != true).Select(p => p.Text));
    }

    // ---------- HTTP with retry ----------

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        using var response = await SendWithRetryAsync(path, body, HttpCompletionOption.ResponseContentRead, ct);
        var result = await response.Content.ReadFromJsonAsync<T>(Json, ct);
        return result ?? throw new GeminiException("Gemini returned an empty response.");
    }

    /// <summary>Sends a POST and retries on 429 / 5xx. The caller owns (and disposes) the response.</summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        string path, object body, HttpCompletionOption completion, CancellationToken ct)
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

            var response = await _http.SendAsync(request, completion, ct);
            if (response.IsSuccessStatusCode) return response;

            var error = await response.Content.ReadAsStringAsync(ct);
            var status = (int)response.StatusCode;
            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500;
            response.Dispose();

            if (retryable && attempt < MaxAttempts)
            {
                // Free tier has rate limits: wait 2s, 4s, 8s before trying again.
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning("Gemini returned {Status}; retrying in {Delay}s (attempt {Attempt})",
                    status, delay.TotalSeconds, attempt);
                await Task.Delay(delay, ct);
                continue;
            }

            throw new GeminiException($"Gemini API error {status}: {Shorten(error)}");
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
