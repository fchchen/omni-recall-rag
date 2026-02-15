namespace OmniRecall.Api.Services;

public sealed class IngestionOptions
{
    public int ChunkSizeWords { get; init; } = 120;
    public int ChunkOverlapWords { get; init; } = 24;
    public long MaxUploadBytes { get; init; } = 10 * 1024 * 1024;
    public int EmbeddingParallelism { get; init; } = 3;
}
