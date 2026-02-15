namespace OmniRecall.Api.Services;

public interface IRawDocumentStore
{
    Task<string> SaveAsync(
        string fileName,
        string content,
        string contentHash,
        CancellationToken cancellationToken = default);
}
