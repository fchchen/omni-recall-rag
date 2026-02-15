using System.Collections.Concurrent;
using OmniRecall.Api.Data.Models;

namespace OmniRecall.Api.Services;

public sealed class InMemoryIngestionStore : IIngestionStore
{
    private readonly ConcurrentDictionary<string, CosmosDocumentRecord> _documents = new();
    private readonly ConcurrentDictionary<string, List<CosmosChunkRecord>> _chunksByDocument = new();

    public Task<CosmosDocumentRecord> UpsertDocumentAsync(CosmosDocumentRecord document, CancellationToken cancellationToken = default)
    {
        _documents[document.Id] = document;
        return Task.FromResult(document);
    }

    public Task UpsertChunksAsync(IReadOnlyList<CosmosChunkRecord> chunks, CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
            return Task.CompletedTask;

        var documentId = chunks[0].DocumentId;
        _chunksByDocument[documentId] = chunks.OrderBy(c => c.ChunkIndex).ToList();
        return Task.CompletedTask;
    }

    public Task<CosmosDocumentRecord?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        _documents.TryGetValue(documentId, out var document);
        return Task.FromResult(document);
    }

    public Task<IReadOnlyList<CosmosDocumentRecord>> ListDocumentsAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        var items = _documents.Values
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(Math.Max(1, maxCount))
            .ToList();
        return Task.FromResult<IReadOnlyList<CosmosDocumentRecord>>(items);
    }

    public Task<IReadOnlyList<CosmosChunkRecord>> GetChunksByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (_chunksByDocument.TryGetValue(documentId, out var chunks))
            return Task.FromResult<IReadOnlyList<CosmosChunkRecord>>(chunks);

        return Task.FromResult<IReadOnlyList<CosmosChunkRecord>>([]);
    }

    public Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        _documents.TryRemove(documentId, out _);
        _chunksByDocument.TryRemove(documentId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CosmosChunkRecord>> GetRecentChunksAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        var all = _chunksByDocument.Values
            .SelectMany(v => v)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(Math.Max(1, maxCount))
            .ToList();
        return Task.FromResult<IReadOnlyList<CosmosChunkRecord>>(all);
    }

    public Task<IReadOnlyDictionary<string, CosmosDocumentRecord>> GetDocumentsByIdsAsync(
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        var set = new HashSet<string>(documentIds);
        var results = _documents
            .Where(kv => set.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return Task.FromResult<IReadOnlyDictionary<string, CosmosDocumentRecord>>(results);
    }
}
