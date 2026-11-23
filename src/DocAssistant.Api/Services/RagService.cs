using System.Text;
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
/// </summary>
public class RagService : IRagService
{
    private const string SystemInstruction =
        """
        You are a document assistant. Answer the user's question using ONLY the numbered sources provided.
        Cite the sources you use inline, like [1] or [2][3], right after the sentence they support.
        If the sources do not contain the answer, say clearly that you could not find it in the documents.
        Do not use outside knowledge and do not invent citations.
        Answer in the same language as the question.
        """;

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
        var question = request.Question.Trim();
        var topK = Math.Clamp(request.TopK ?? _options.TopK, 1, 20);

        // 1) Retrieve
        var queryVector = await _embeddings.EmbedQueryAsync(question, ct);
        var hits = (await _vectors.SearchAsync(queryVector, topK, request.DocumentIds, ct))
            .Where(h => h.Score >= _options.MinScore)
            .ToList();

        _logger.LogInformation("Question retrieved {Count} chunks (best score {Best:F3})",
            hits.Count, hits.Count > 0 ? hits[0].Score : 0);

        if (hits.Count == 0)
        {
            return new ChatResponse(
                "I couldn't find anything related to this question in your documents.",
                Array.Empty<SourceDto>());
        }

        // 2) Build the prompt with numbered sources
        var prompt = new StringBuilder("Sources:\n\n");
        for (var i = 0; i < hits.Count; i++)
        {
            var h = hits[i];
            prompt.Append($"[{i + 1}] (File: {h.FileName}, page {h.PageNumber})\n{h.Text}\n\n");
        }
        prompt.Append($"Question: {question}");

        // 3) Generate
        var answer = await _chat.GenerateAsync(SystemInstruction, prompt.ToString(), ct);

        var sources = hits.Select((h, i) => new SourceDto(
            Number: i + 1,
            DocumentId: h.DocumentId,
            FileName: h.FileName,
            PageNumber: h.PageNumber,
            Score: Math.Round(h.Score, 3),
            Snippet: h.Text.Length <= 300 ? h.Text : h.Text[..300] + "..."))
            .ToList();

        return new ChatResponse(answer, sources);
    }
}
