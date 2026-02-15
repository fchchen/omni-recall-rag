namespace OmniRecall.Api.Services;

public sealed class NoOpOcrTextExtractor : IOcrTextExtractor
{
    public Task<string> ExtractTextAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
    }
}
