using System.Collections.Concurrent;

namespace OmniRecall.Api.Services;

public sealed class InMemoryRawDocumentStore : IRawDocumentStore
{
    private readonly ConcurrentDictionary<string, string> _contentByPath = new();

    public Task<string> SaveAsync(
        string fileName,
        string content,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        var safeName = fileName.Replace(' ', '-').ToLowerInvariant();
        var path = $"raw/{safeName}";
        _contentByPath[path] = content;
        return Task.FromResult(path);
    }
}
