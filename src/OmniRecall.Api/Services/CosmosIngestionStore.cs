using Microsoft.Azure.Cosmos;
using OmniRecall.Api.Data.Models;
using System.Net;

namespace OmniRecall.Api.Services;

public sealed class CosmosIngestionStore : IIngestionStore, IDisposable
{
    private const int MaxBatchItemCount = 100;

    private readonly CosmosClient _client;
    private readonly Container _documentsContainer;
    private readonly Container _chunksContainer;

    public CosmosIngestionStore(IConfiguration configuration, ILogger<CosmosIngestionStore> logger)
    {
        var connectionString = configuration["AzureCosmos:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("AzureCosmos:ConnectionString is required for Azure storage provider.");

        var databaseName = configuration["AzureCosmos:DatabaseName"];
        if (string.IsNullOrWhiteSpace(databaseName))
            databaseName = "omni-recall";

        var documentsContainerName = configuration["AzureCosmos:DocumentsContainerName"];
        if (string.IsNullOrWhiteSpace(documentsContainerName))
            documentsContainerName = "documents";

        var chunksContainerName = configuration["AzureCosmos:ChunksContainerName"];
        if (string.IsNullOrWhiteSpace(chunksContainerName))
            chunksContainerName = "chunks";

        _client = new CosmosClient(connectionString);
        var database = _client.GetDatabase(databaseName);
        _documentsContainer = database.GetContainer(documentsContainerName);
        _chunksContainer = database.GetContainer(chunksContainerName);

        logger.LogInformation(
            "Initialized Cosmos ingestion store with database {Database} and containers {Documents}/{Chunks}",
            databaseName,
            documentsContainerName,
            chunksContainerName);
    }

    public async Task<CosmosDocumentRecord> UpsertDocumentAsync(CosmosDocumentRecord document, CancellationToken cancellationToken = default)
    {
        await _documentsContainer.UpsertItemAsync(document, new PartitionKey(document.PartitionKey), cancellationToken: cancellationToken);
        return document;
    }

    public async Task UpsertChunksAsync(IReadOnlyList<CosmosChunkRecord> chunks, CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
            return;

        foreach (var partitionGroup in chunks.GroupBy(c => c.PartitionKey, StringComparer.Ordinal))
        {
            var items = partitionGroup.ToList();
            var partitionKey = new PartitionKey(partitionGroup.Key);

            for (var offset = 0; offset < items.Count; offset += MaxBatchItemCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var upper = Math.Min(offset + MaxBatchItemCount, items.Count);
                var batch = _chunksContainer.CreateTransactionalBatch(partitionKey);
                for (var i = offset; i < upper; i++)
                {
                    batch.UpsertItem(items[i]);
                }

                var response = await batch.ExecuteAsync(cancellationToken);
                EnsureBatchSucceeded(response, "upsert chunks");
            }
        }
    }

    public async Task<CosmosDocumentRecord?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _documentsContainer.ReadItemAsync<CosmosDocumentRecord>(
                documentId,
                new PartitionKey("user:default"),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<CosmosDocumentRecord>> ListDocumentsAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
            "SELECT TOP @maxCount * FROM c WHERE c.type = @type ORDER BY c.createdAtUtc DESC")
            .WithParameter("@maxCount", Math.Max(1, maxCount))
            .WithParameter("@type", "document");

        var iterator = _documentsContainer.GetItemQueryIterator<CosmosDocumentRecord>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("user:default") });

        var results = new List<CosmosDocumentRecord>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page);
        }

        return results;
    }

    public async Task<IReadOnlyList<CosmosChunkRecord>> GetChunksByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = @type AND c.documentId = @documentId ORDER BY c.chunkIndex")
            .WithParameter("@type", "chunk")
            .WithParameter("@documentId", documentId);

        var iterator = _chunksContainer.GetItemQueryIterator<CosmosChunkRecord>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("user:default") });

        var results = new List<CosmosChunkRecord>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page);
        }

        return results;
    }

    public async Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var chunks = await GetChunksByDocumentIdAsync(documentId, cancellationToken);
        foreach (var partitionGroup in chunks.GroupBy(c => c.PartitionKey, StringComparer.Ordinal))
        {
            var items = partitionGroup.ToList();
            var partitionKey = new PartitionKey(partitionGroup.Key);
            for (var offset = 0; offset < items.Count; offset += MaxBatchItemCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var upper = Math.Min(offset + MaxBatchItemCount, items.Count);
                var batch = _chunksContainer.CreateTransactionalBatch(partitionKey);
                for (var i = offset; i < upper; i++)
                {
                    batch.DeleteItem(items[i].Id);
                }

                var response = await batch.ExecuteAsync(cancellationToken);
                if (!response.IsSuccessStatusCode && !IsIgnorableDeleteBatchResponse(response))
                    EnsureBatchSucceeded(response, "delete chunks");
            }
        }

        try
        {
            await _documentsContainer.DeleteItemAsync<CosmosDocumentRecord>(
                documentId,
                new PartitionKey("user:default"),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Ignore item missing during delete.
        }
    }

    public async Task<IReadOnlyList<CosmosChunkRecord>> GetRecentChunksAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
            "SELECT TOP @maxCount * FROM c WHERE c.type = @type ORDER BY c.createdAtUtc DESC")
            .WithParameter("@maxCount", Math.Max(1, maxCount))
            .WithParameter("@type", "chunk");

        var iterator = _chunksContainer.GetItemQueryIterator<CosmosChunkRecord>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("user:default") });

        var results = new List<CosmosChunkRecord>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page);
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<string, CosmosDocumentRecord>> GetDocumentsByIdsAsync(
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        var ids = documentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
            return new Dictionary<string, CosmosDocumentRecord>(StringComparer.Ordinal);

        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = @type AND ARRAY_CONTAINS(@ids, c.id)")
            .WithParameter("@type", "document")
            .WithParameter("@ids", ids);

        var iterator = _documentsContainer.GetItemQueryIterator<CosmosDocumentRecord>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("user:default") });

        var results = new Dictionary<string, CosmosDocumentRecord>(StringComparer.Ordinal);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var item in page)
            {
                results[item.Id] = item;
            }
        }

        return results;
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static void EnsureBatchSucceeded(TransactionalBatchResponse response, string operation)
    {
        if (response.IsSuccessStatusCode)
            return;

        throw new InvalidOperationException(
            $"Cosmos transactional batch failed while trying to {operation}. " +
            $"Status: {(int)response.StatusCode} ({response.StatusCode}).");
    }

    private static bool IsIgnorableDeleteBatchResponse(TransactionalBatchResponse response)
    {
        for (var i = 0; i < response.Count; i++)
        {
            var status = response[i].StatusCode;
            if ((int)status is >= 200 and < 300)
                continue;

            if (status == HttpStatusCode.NotFound)
                continue;

            return false;
        }

        return true;
    }
}
