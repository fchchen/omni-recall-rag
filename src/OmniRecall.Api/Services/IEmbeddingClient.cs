namespace OmniRecall.Api.Services;

public enum EmbeddingStatus
{
    Success,
    Empty,
    RateLimited,
    NotSupported,
    Error
}

public sealed record EmbeddingResult(
    IReadOnlyList<float> Vector,
    EmbeddingStatus Status,
    string? Model = null,
    string? Message = null);

public interface IEmbeddingClient
{
    Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
