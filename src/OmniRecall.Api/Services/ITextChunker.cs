namespace OmniRecall.Api.Services;

public interface ITextChunker
{
    IReadOnlyList<string> Chunk(string text, int chunkSizeWords, int chunkOverlapWords);
}
