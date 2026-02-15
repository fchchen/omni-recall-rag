using OmniRecall.Api.Data.Models;

namespace OmniRecall.Api.Services;

public interface IIngestionStore
{
    Task<CosmosDocumentRecord> UpsertDocumentAsync(CosmosDocumentRecord document, CancellationToken cancellationToken = default);
    Task UpsertChunksAsync(IReadOnlyList<CosmosChunkRecord> chunks, CancellationToken cancellationToken = default);
    Task<CosmosDocumentRecord?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CosmosDocumentRecord>> ListDocumentsAsync(int maxCount, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CosmosChunkRecord>> GetChunksByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default);
    Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CosmosChunkRecord>> GetRecentChunksAsync(int maxCount, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, CosmosDocumentRecord>> GetDocumentsByIdsAsync(
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken = default);
}
