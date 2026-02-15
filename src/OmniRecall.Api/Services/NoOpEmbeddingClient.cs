namespace OmniRecall.Api.Services;

public sealed class NoOpEmbeddingClient : IEmbeddingClient
{
    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EmbeddingResult([], EmbeddingStatus.Empty, "none", "Embedding provider disabled."));
    }
}
