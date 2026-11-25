using System.Runtime.CompilerServices;
using DocAssistant.Api.DTOs;
using DocAssistant.Api.Options;
using DocAssistant.Api.Services;
using DocAssistant.Api.Services.Ai;
using DocAssistant.Api.Services.VectorStore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocAssistant.Tests;

/// <summary>
/// RagService is tested with small fakes instead of Gemini and Qdrant,
/// which is possible because it only depends on interfaces.
/// </summary>
public class RagServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid DocA = Guid.NewGuid();
    private static readonly Guid DocB = Guid.NewGuid();

    [Fact]
    public async Task AskAsync_ReturnsOnlyTheSourcesTheAnswerCites()
    {
        var chat = new FakeChatModel("Algorithm Consulting [2], and later Geeks of Kolachi [3][2].");
        var rag = CreateService(owned: new[] { DocA }, chat: chat, hits: ThreeHits(DocA));

        var response = await rag.AskAsync(new ChatRequest("Where did he work?"), UserId, default);

        Assert.Equal(new[] { 2, 3 }, response.Sources.Select(s => s.Number));
        Assert.Equal(chat.Answer, response.Answer);
    }

    [Fact]
    public async Task AskAsync_AnswerWithoutCitations_ReturnsNoSources()
    {
        var chat = new FakeChatModel("I could not find this in the documents.");
        var rag = CreateService(owned: new[] { DocA }, chat: chat, hits: ThreeHits(DocA));

        var response = await rag.AskAsync(new ChatRequest("What is his salary?"), UserId, default);

        Assert.Empty(response.Sources);
    }

    [Fact]
    public async Task AskAsync_UserHasNoDocuments_DoesNotCallTheModelOrVectorStore()
    {
        var chat = new FakeChatModel("should not be used");
        var vectors = new FakeVectorStore(ThreeHits(DocA));
        var rag = CreateService(owned: Array.Empty<Guid>(), chat: chat, vectors: vectors);

        var response = await rag.AskAsync(new ChatRequest("Anything?"), UserId, default);

        Assert.Contains("couldn't find", response.Answer);
        Assert.Equal(0, chat.Calls);
        Assert.Null(vectors.LastFilter);
    }

    [Fact]
    public async Task AskAsync_RequestedDocumentsAreLimitedToTheUsersOwn()
    {
        var vectors = new FakeVectorStore(ThreeHits(DocA));
        var rag = CreateService(owned: new[] { DocA }, vectors: vectors);

        // DocB belongs to someone else: it must never reach the vector search.
        await rag.AskAsync(new ChatRequest("Question", new List<Guid> { DocA, DocB }), UserId, default);

        Assert.Equal(new[] { DocA }, vectors.LastFilter);
    }

    [Fact]
    public async Task AskAsync_OnlyForeignDocumentRequested_ReturnsNotFound()
    {
        var vectors = new FakeVectorStore(ThreeHits(DocB));
        var rag = CreateService(owned: new[] { DocA }, vectors: vectors);

        var response = await rag.AskAsync(new ChatRequest("Question", new List<Guid> { DocB }), UserId, default);

        Assert.Contains("couldn't find", response.Answer);
        Assert.Null(vectors.LastFilter);
    }

    [Fact]
    public async Task AskStreamAsync_StreamsTextPiecesThenTheCitedSources()
    {
        var chat = new FakeChatModel("Skills: C# [1] and React [3].");
        var rag = CreateService(owned: new[] { DocA }, chat: chat, hits: ThreeHits(DocA));

        var events = new List<ChatStreamEvent>();
        await foreach (var e in rag.AskStreamAsync(new ChatRequest("Skills?"), UserId, default))
            events.Add(e);

        var deltas = events.Where(e => e.Type == "delta").Select(e => e.Text).ToList();
        Assert.True(deltas.Count > 1, "The answer should arrive in several pieces");
        Assert.Equal(chat.Answer, string.Concat(deltas));

        var last = events[^1];
        Assert.Equal("sources", last.Type);
        Assert.Equal(new[] { 1, 3 }, last.Sources!.Select(s => s.Number));
    }

    // ---------- helpers ----------

    private static RagService CreateService(
        IReadOnlyList<Guid> owned,
        FakeChatModel? chat = null,
        FakeVectorStore? vectors = null,
        IReadOnlyList<SearchHit>? hits = null) =>
        new(
            new FakeScope(owned),
            new FakeEmbeddings(),
            vectors ?? new FakeVectorStore(hits ?? Array.Empty<SearchHit>()),
            chat ?? new FakeChatModel("Answer [1]."),
            Options.Create(new RagOptions { TopK = 5, MinScore = 0 }),
            NullLogger<RagService>.Instance);

    private static IReadOnlyList<SearchHit> ThreeHits(Guid documentId) => new[]
    {
        new SearchHit(documentId, 0, 1, "cv.pdf", "Summary text", 0.80),
        new SearchHit(documentId, 1, 1, "cv.pdf", "Algorithm Consulting, Software Engineer", 0.75),
        new SearchHit(documentId, 2, 2, "cv.pdf", "Geeks of Kolachi, Intern", 0.70),
    };

    private sealed class FakeScope(IReadOnlyList<Guid> owned) : IDocumentScope
    {
        public Task<IReadOnlyList<Guid>> GetReadyDocumentIdsAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult(owned);
    }

    private sealed class FakeEmbeddings : IEmbeddingService
    {
        public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f }).ToList());

        public Task<float[]> EmbedQueryAsync(string text, CancellationToken ct) => Task.FromResult(new[] { 1f });
    }

    private sealed class FakeVectorStore(IReadOnlyList<SearchHit> hits) : IVectorStore
    {
        public IReadOnlyCollection<Guid>? LastFilter { get; private set; }

        public Task EnsureCollectionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task UpsertAsync(IReadOnlyList<VectorPoint> points, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteByDocumentAsync(Guid documentId, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<SearchHit>> SearchAsync(
            float[] vector, int limit, IReadOnlyCollection<Guid>? documentIds, CancellationToken ct)
        {
            LastFilter = documentIds;
            return Task.FromResult<IReadOnlyList<SearchHit>>(hits.Take(limit).ToList());
        }
    }

    private sealed class FakeChatModel(string answer) : IChatModel
    {
        public string Answer { get; } = answer;
        public int Calls { get; private set; }

        public Task<string> GenerateAsync(string systemInstruction, string prompt, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Answer);
        }

        public async IAsyncEnumerable<string> StreamAsync(
            string systemInstruction, string prompt, [EnumeratorCancellation] CancellationToken ct)
        {
            Calls++;
            // Send the answer in pieces of 5 characters, like a real streaming model.
            for (var i = 0; i < Answer.Length; i += 5)
            {
                await Task.Yield();
                yield return Answer.Substring(i, Math.Min(5, Answer.Length - i));
            }
        }
    }
}
