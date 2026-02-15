using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OmniRecall.Api.Data.Models;

namespace OmniRecall.Api.Services;

public interface IDocumentIngestionService
{
    Task<DocumentIngestionResult> IngestAsync(
        string fileName,
        string content,
        string sourceType,
        CancellationToken cancellationToken = default);

    Task<DocumentIngestionDetails?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentSummary>> ListDocumentsAsync(int maxCount, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentChunkPreview>> GetDocumentChunksAsync(string documentId, int maxCount, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task<DocumentReindexResult?> ReindexDocumentAsync(string documentId, CancellationToken cancellationToken = default);
}

public sealed record DocumentIngestionResult(
    string DocumentId,
    string FileName,
    string SourceType,
    string BlobPath,
    int ChunkCount,
    string ContentHash,
    DateTime CreatedAtUtc);

public sealed record DocumentIngestionDetails(
    string DocumentId,
    string FileName,
    string SourceType,
    string BlobPath,
    int ChunkCount,
    string ContentHash,
    DateTime CreatedAtUtc);

public sealed record DocumentSummary(
    string DocumentId,
    string FileName,
    string SourceType,
    int ChunkCount,
    DateTime CreatedAtUtc);

public sealed record DocumentChunkPreview(
    string ChunkId,
    int ChunkIndex,
    string Snippet,
    bool HasEmbedding,
    DateTime CreatedAtUtc);

public sealed record DocumentReindexResult(
    string DocumentId,
    int ChunkCount,
    int EmbeddedCount,
    int RateLimitedCount,
    int EmptyCount,
    int FailedCount,
    DateTime ReindexedAtUtc);

public sealed class DocumentIngestionService(
    ITextChunker chunker,
    IIngestionStore store,
    IRawDocumentStore rawDocumentStore,
    IEmbeddingClient embeddingClient,
    IOptions<IngestionOptions> options,
    ILogger<DocumentIngestionService> logger) : IDocumentIngestionService
{
    public async Task<DocumentIngestionResult> IngestAsync(
        string fileName,
        string content,
        string sourceType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required.", nameof(content));

        var normalized = content.Replace("\r\n", "\n").Trim();
        var contentHash = ComputeSha256(normalized);
        var existing = await FindExistingByContentHashAsync(contentHash, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation(
                "Deduplicated ingest for {FileName}; returning existing document {DocumentId} for matching content hash.",
                fileName,
                existing.Id);
            return new DocumentIngestionResult(
                existing.Id,
                existing.FileName,
                existing.SourceType,
                existing.BlobPath,
                existing.ChunkCount,
                existing.ContentHash,
                existing.CreatedAtUtc);
        }

        var createdAtUtc = DateTime.UtcNow;
        var documentId = $"doc_{Guid.NewGuid():N}";
        var blobPath = await rawDocumentStore.SaveAsync(fileName, normalized, contentHash, cancellationToken);

        var chunkTexts = chunker.Chunk(
            normalized,
            options.Value.ChunkSizeWords,
            options.Value.ChunkOverlapWords);

        if (chunkTexts.Count == 0)
            throw new InvalidOperationException("No chunks produced for document.");

        var embeddings = await EmbedTextsAsync(
            chunkTexts,
            contextId: fileName,
            operation: "ingest",
            cancellationToken);

        var chunks = new List<CosmosChunkRecord>(chunkTexts.Count);
        for (var index = 0; index < chunkTexts.Count; index++)
        {
            var text = chunkTexts[index];
            var embedding = embeddings[index];
            chunks.Add(new CosmosChunkRecord
            {
                Id = $"{documentId}:{index:D4}",
                DocumentId = documentId,
                ChunkIndex = index,
                Content = text,
                Embedding = embedding.Vector,
                CreatedAtUtc = createdAtUtc
            });
        }

        var document = new CosmosDocumentRecord
        {
            Id = documentId,
            FileName = fileName,
            SourceType = sourceType,
            BlobPath = blobPath,
            ContentHash = contentHash,
            ChunkCount = chunkTexts.Count,
            CreatedAtUtc = createdAtUtc
        };

        await store.UpsertDocumentAsync(document, cancellationToken);
        await store.UpsertChunksAsync(chunks, cancellationToken);

        logger.LogInformation("Ingested document {DocumentId} ({ChunkCount} chunks).", documentId, chunkTexts.Count);

        return new DocumentIngestionResult(
            documentId,
            fileName,
            sourceType,
            blobPath,
            chunkTexts.Count,
            contentHash,
            createdAtUtc);
    }

    public async Task<DocumentIngestionDetails?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var document = await store.GetDocumentAsync(documentId, cancellationToken);
        if (document is null)
            return null;

        return new DocumentIngestionDetails(
            document.Id,
            document.FileName,
            document.SourceType,
            document.BlobPath,
            document.ChunkCount,
            document.ContentHash,
            document.CreatedAtUtc);
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListDocumentsAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        var docs = await store.ListDocumentsAsync(maxCount, cancellationToken);
        return docs
            .OrderByDescending(d => d.CreatedAtUtc)
            .Select(d => new DocumentSummary(
                d.Id,
                d.FileName,
                d.SourceType,
                d.ChunkCount,
                d.CreatedAtUtc))
            .ToList();
    }

    public async Task<IReadOnlyList<DocumentChunkPreview>> GetDocumentChunksAsync(
        string documentId,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        var chunks = await store.GetChunksByDocumentIdAsync(documentId, cancellationToken);
        return chunks
            .OrderBy(c => c.ChunkIndex)
            .Take(Math.Max(1, maxCount))
            .Select(c => new DocumentChunkPreview(
                c.Id,
                c.ChunkIndex,
                TextSnippetHelper.BuildSnippet(c.Content, 220),
                c.Embedding is { Count: > 0 },
                c.CreatedAtUtc))
            .ToList();
    }

    public async Task<bool> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var existing = await store.GetDocumentAsync(documentId, cancellationToken);
        if (existing is null)
            return false;

        await store.DeleteDocumentAsync(documentId, cancellationToken);
        return true;
    }

    public async Task<DocumentReindexResult?> ReindexDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var document = await store.GetDocumentAsync(documentId, cancellationToken);
        if (document is null)
            return null;

        var chunks = await store.GetChunksByDocumentIdAsync(documentId, cancellationToken);
        if (chunks.Count == 0)
        {
            return new DocumentReindexResult(documentId, 0, 0, 0, 0, 0, DateTime.UtcNow);
        }

        var reindexedAt = DateTime.UtcNow;
        var embeddedCount = 0;
        var rateLimitedCount = 0;
        var emptyCount = 0;
        var failedCount = 0;
        var orderedChunks = chunks.OrderBy(c => c.ChunkIndex).ToList();
        var embeddings = await EmbedTextsAsync(
            orderedChunks.Select(c => c.Content).ToList(),
            contextId: documentId,
            operation: "reindex",
            cancellationToken);

        var updatedChunks = new List<CosmosChunkRecord>(orderedChunks.Count);
        for (var index = 0; index < orderedChunks.Count; index++)
        {
            var chunk = orderedChunks[index];
            var embedding = embeddings[index];

            var newVector = chunk.Embedding;
            switch (embedding.Status)
            {
                case EmbeddingStatus.Success when embedding.Vector.Count > 0:
                    embeddedCount++;
                    newVector = embedding.Vector;
                    break;
                case EmbeddingStatus.RateLimited:
                    rateLimitedCount++;
                    break;
                case EmbeddingStatus.Error:
                    failedCount++;
                    break;
                default:
                    emptyCount++;
                    break;
            }

            updatedChunks.Add(new CosmosChunkRecord
            {
                Id = chunk.Id,
                PartitionKey = chunk.PartitionKey,
                Type = chunk.Type,
                DocumentId = chunk.DocumentId,
                ChunkIndex = chunk.ChunkIndex,
                Content = chunk.Content,
                Embedding = newVector,
                CreatedAtUtc = chunk.CreatedAtUtc
            });
        }

        await store.UpsertChunksAsync(updatedChunks, cancellationToken);

        return new DocumentReindexResult(
            documentId,
            updatedChunks.Count,
            embeddedCount,
            rateLimitedCount,
            emptyCount,
            failedCount,
            reindexedAt);
    }

    private static string ComputeSha256(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<CosmosDocumentRecord?> FindExistingByContentHashAsync(
        string contentHash,
        CancellationToken cancellationToken)
    {
        // Personal-scale dedupe check; keeps ingestion idempotent for repeated uploads.
        var candidates = await store.ListDocumentsAsync(1000, cancellationToken);
        return candidates.FirstOrDefault(d =>
            string.Equals(d.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<EmbeddingResult>> EmbedTextsAsync(
        IReadOnlyList<string> texts,
        string contextId,
        string operation,
        CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
            return [];

        var maxParallelism = Math.Clamp(options.Value.EmbeddingParallelism, 1, 8);
        var results = new EmbeddingResult[texts.Count];
        using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);

        var tasks = texts
            .Select((text, index) => EmbedOneAsync(text, index, contextId, operation, semaphore, results, cancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);
        return results;
    }

    private async Task EmbedOneAsync(
        string text,
        int chunkIndex,
        string contextId,
        string operation,
        SemaphoreSlim semaphore,
        EmbeddingResult[] results,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[chunkIndex] = await embeddingClient.EmbedAsync(text, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Embedding generation failed during {Operation} for {ContextId} chunk {ChunkIndex}",
                operation,
                contextId,
                chunkIndex);
            results[chunkIndex] = new EmbeddingResult([], EmbeddingStatus.Error, Message: ex.Message);
        }
        finally
        {
            semaphore.Release();
        }
    }

}
