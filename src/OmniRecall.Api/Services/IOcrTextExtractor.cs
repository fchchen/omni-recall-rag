namespace OmniRecall.Api.Services;

public interface IOcrTextExtractor
{
    Task<string> ExtractTextAsync(Stream fileStream, CancellationToken cancellationToken = default);
}
