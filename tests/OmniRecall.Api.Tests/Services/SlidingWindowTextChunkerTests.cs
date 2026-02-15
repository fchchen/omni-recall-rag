using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Services;

public class SlidingWindowTextChunkerTests
{
    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var sut = new SlidingWindowTextChunker();

        var chunks = sut.Chunk("one two three", 10, 2);

        Assert.Single(chunks);
        Assert.Equal("one two three", chunks[0]);
    }

    [Fact]
    public void Chunk_LongText_WithOverlap_ReturnsMultipleChunks()
    {
        var sut = new SlidingWindowTextChunker();
        var text = string.Join(' ', Enumerable.Range(1, 20).Select(i => $"word{i}"));

        var chunks = sut.Chunk(text, 8, 2);

        Assert.True(chunks.Count >= 3);
        Assert.Contains("word7", chunks[1]);
        Assert.Contains("word8", chunks[1]);
    }
}
