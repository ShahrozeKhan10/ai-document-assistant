using DocAssistant.Api.Options;
using DocAssistant.Api.Services;
using Microsoft.Extensions.Options;

namespace DocAssistant.Tests;

public class TextChunkerTests
{
    private static TextChunker CreateChunker(int size = 200, int overlap = 50) =>
        new(Options.Create(new ChunkingOptions { ChunkSize = size, ChunkOverlap = overlap }));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void Chunk_EmptyOrWhitespace_ReturnsNoChunks(string text)
    {
        Assert.Empty(CreateChunker().Chunk(text));
    }

    [Fact]
    public void Chunk_ShortText_ReturnsOneChunkWithNormalizedWhitespace()
    {
        var chunks = CreateChunker().Chunk("Hello   world.\n\nThis   is\ta test.");

        var chunk = Assert.Single(chunks);
        Assert.Equal("Hello world. This is a test.", chunk);
    }

    [Fact]
    public void Chunk_LongText_NoChunkIsLongerThanTheChunkSize()
    {
        var chunks = CreateChunker(size: 200).Chunk(MakeWords(600));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 200, $"Chunk was {c.Length} chars"));
    }

    [Fact]
    public void Chunk_LongText_NeighbouringChunksOverlap()
    {
        var chunks = CreateChunker(size: 200, overlap: 50).Chunk(MakeWords(600));

        for (var i = 0; i < chunks.Count - 1; i++)
        {
            // The start of each chunk should repeat text from the end of the previous one.
            var startOfNext = chunks[i + 1][..15];
            Assert.Contains(startOfNext, chunks[i]);
        }
    }

    [Fact]
    public void Chunk_TextWithSentences_EndsChunksAtSentenceBoundaries()
    {
        var text = string.Join(" ", Enumerable.Range(1, 40).Select(n => $"This is sentence number {n}."));

        var chunks = CreateChunker(size: 200, overlap: 50).Chunk(text);

        Assert.True(chunks.Count > 1);
        // Every chunk except the last should end with a full sentence.
        Assert.All(chunks.Take(chunks.Count - 1), c => Assert.EndsWith(".", c));
    }

    [Fact]
    public void Chunk_TextWithoutSpaces_StillSplitsWithoutLosingText()
    {
        // With no spaces there is no word boundary to step back to, so chunks are exact slices.
        var text = new string('x', 1000);

        var chunks = CreateChunker(size: 200, overlap: 50).Chunk(text);

        Assert.Equal(5, chunks.Count);
        Assert.Equal(text, string.Concat(chunks));
    }

    /// <summary>Words of 3–7 letters, separated by spaces, with no sentence punctuation.</summary>
    private static string MakeWords(int count)
    {
        string[] words = { "wind", "turbine", "power", "curve", "blade", "rotor", "grid", "speed" };
        return string.Join(" ", Enumerable.Range(0, count).Select(i => words[i % words.Length]));
    }
}
