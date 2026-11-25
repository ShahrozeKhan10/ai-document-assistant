using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using DocAssistant.Api.DTOs;
using DocAssistant.Api.Options;
using DocAssistant.Api.Services.Ai;
using DocAssistant.Api.Services.VectorStore;
using Microsoft.Extensions.Options;

namespace DocAssistant.Api.Services;

/// <summary>
/// Retrieval-Augmented Generation:
///   1. embed the question
///   2. retrieve the closest chunks from Qdrant
///   3. give those chunks to Gemini as numbered sources and ask it to answer only from them
///   4. return only the sources the answer actually cited
/// </summary>
public partial class RagService : IRagService
{
    private const string NotFoundAnswer = "I couldn't find anything related to this question in your documents.";

    private const string SystemInstruction =
        """
        You are a document assistant. Answer the user's question using ONLY the numbered sources provided.
        Cite the sources you use inline, like [1] or [2][3], right after the sentence they support.
        If the sources do not contain the answer, say clearly that you could not find it in the documents.
        Do not use outside knowledge and do not invent citations.
        Answer in the same language as the question.
        """;

    // Matches [1], [12], and lists like [1, 2]
    [GeneratedRegex(@"\[(\d+(?:\s*,\s*\d+)*)\]")]
    private static partial Regex CitationRegex();

    private readonly IEmbeddingService _embeddings;
    private readonly IVectorStore _vectors;
    private readonly IChatModel _chat;
    private readonly RagOptions _options;
    private readonly ILogger<RagService> _logger;

    public RagService(
        IEmbeddingService embeddings,
        IVectorStore vectors,
        IChatModel chat,
        IOptions<RagOptions> options,
        ILogger<RagService> logger)
    {
        _embeddings = embeddings;
        _vectors = vectors;
        _chat = chat;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken ct)
    {
        var hits = await RetrieveAsync(request, ct);
        if (hits.Count == 0) return new ChatResponse(NotFoundAnswer, Array.Empty<SourceDto>());

        var answer = await _chat.GenerateAsync(SystemInstruction, BuildPrompt(request.Question, hits), ct);
        return new ChatResponse(answer, CitedSources(answer, hits));
    }

    public async IAsyncEnumerable<ChatStreamEvent> AskStreamAsync(
        ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var hits = await RetrieveAsync(request, ct);
        if (hits.Count == 0)
        {
            yield return new ChatStreamEvent("delta", Text: NotFoundAnswer);
            yield return new ChatStreamEvent("sources", Sources: Array.Empty<SourceDto>());
            yield break;
        }

        var answer = new StringBuilder();
        await foreach (var piece in _chat.StreamAsync(SystemInstruction, BuildPrompt(request.Question, hits), ct))
        {
            answer.Append(piece);
            yield return new ChatStreamEvent("delta", Text: piece);
        }

        // Only now do we know which sources the answer used.
        yield return new ChatStreamEvent("sources", Sources: CitedSources(answer.ToString(), hits));
    }

    private async Task<List<SearchHit>> RetrieveAsync(ChatRequest request, CancellationToken ct)
    {
        var topK = Math.Clamp(request.TopK ?? _options.TopK, 1, 20);

        var queryVector = await _embeddings.EmbedQueryAsync(request.Question.Trim(), ct);
        var hits = (await _vectors.SearchAsync(queryVector, topK, request.DocumentIds, ct))
            .Where(h => h.Score >= _options.MinScore)
            .ToList();

        _logger.LogInformation("Question retrieved {Count} chunks (best score {Best:F3})",
            hits.Count, hits.Count > 0 ? hits[0].Score : 0);

        return hits;
    }

    private static string BuildPrompt(string question, IReadOnlyList<SearchHit> hits)
    {
        var prompt = new StringBuilder("Sources:\n\n");
        for (var i = 0; i < hits.Count; i++)
        {
            var h = hits[i];
            prompt.Append($"[{i + 1}] (File: {h.FileName}, page {h.PageNumber})\n{h.Text}\n\n");
        }
        prompt.Append($"Question: {question.Trim()}");
        return prompt.ToString();
    }

    /// <summary>
    /// Keeps only the sources whose number appears in the answer, e.g. "... [2][3]" → sources 2 and 3.
    /// Numbers stay the same as in the prompt, so [3] in the text always means source 3.
    /// </summary>
    private static IReadOnlyList<SourceDto> CitedSources(string answer, IReadOnlyList<SearchHit> hits)
    {
        var cited = CitationRegex().Matches(answer)
            .SelectMany(m => m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries))
            .Select(n => int.TryParse(n, out var v) ? v : 0)
            .Where(n => n >= 1 && n <= hits.Count)
            .ToHashSet();

        return cited
            .Order()
            .Select(n =>
            {
                var h = hits[n - 1];
                return new SourceDto(
                    Number: n,
                    DocumentId: h.DocumentId,
                    FileName: h.FileName,
                    PageNumber: h.PageNumber,
                    Score: Math.Round(h.Score, 3),
                    Snippet: h.Text.Length <= 300 ? h.Text : h.Text[..300] + "...");
            })
            .ToList();
    }
}
