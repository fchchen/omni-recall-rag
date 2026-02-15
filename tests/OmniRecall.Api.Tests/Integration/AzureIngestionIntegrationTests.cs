using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OmniRecall.Api.Data.Models;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Integration;

public class AzureIngestionIntegrationTests
{
    [Fact]
    public async Task AzureStores_SmokeTest_RoundTripsDocumentMetadata_WhenConfigured()
    {
        var settings = AzureIntegrationSettings.FromEnvironment();
        if (!settings.IsConfigured)
            return;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToConfigurationMap())
            .Build();

        var blobStore = new BlobRawDocumentStore(config, NullLogger<BlobRawDocumentStore>.Instance);
        var cosmosStore = new CosmosIngestionStore(config, NullLogger<CosmosIngestionStore>.Instance);

        var now = DateTime.UtcNow;
        var docId = $"it-doc-{Guid.NewGuid():N}";
        var content = $"integration smoke content {docId}";
        var contentHash = docId[..16];

        var blobPath = await blobStore.SaveAsync($"{docId}.md", content, contentHash);
        await cosmosStore.UpsertDocumentAsync(new CosmosDocumentRecord
        {
            Id = docId,
            FileName = $"{docId}.md",
            SourceType = "integration",
            BlobPath = blobPath,
            ContentHash = contentHash,
            ChunkCount = 1,
            CreatedAtUtc = now
        });

        await cosmosStore.UpsertChunksAsync([
            new CosmosChunkRecord
            {
                Id = $"{docId}:0000",
                DocumentId = docId,
                ChunkIndex = 0,
                Content = content,
                Embedding = [0.1f, 0.2f],
                CreatedAtUtc = now
            }
        ]);

        var saved = await cosmosStore.GetDocumentAsync(docId);
        var recent = await cosmosStore.GetRecentChunksAsync(50);

        Assert.NotNull(saved);
        Assert.Equal(blobPath, saved!.BlobPath);
        Assert.Contains(recent, c => c.DocumentId == docId);
    }
}

internal sealed record AzureIntegrationSettings(
    string CosmosConnectionString,
    string CosmosDatabaseName,
    string CosmosDocumentsContainer,
    string CosmosChunksContainer,
    string BlobConnectionString,
    string BlobContainerName)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CosmosConnectionString) &&
        !string.IsNullOrWhiteSpace(CosmosDatabaseName) &&
        !string.IsNullOrWhiteSpace(CosmosDocumentsContainer) &&
        !string.IsNullOrWhiteSpace(CosmosChunksContainer) &&
        !string.IsNullOrWhiteSpace(BlobConnectionString) &&
        !string.IsNullOrWhiteSpace(BlobContainerName);

    public static AzureIntegrationSettings FromEnvironment()
    {
        return new AzureIntegrationSettings(
            Environment.GetEnvironmentVariable("AZURE_COSMOS_CONNECTION_STRING") ?? "",
            Environment.GetEnvironmentVariable("AZURE_COSMOS_DATABASE") ?? "",
            Environment.GetEnvironmentVariable("AZURE_COSMOS_DOCS_CONTAINER") ?? "",
            Environment.GetEnvironmentVariable("AZURE_COSMOS_CHUNKS_CONTAINER") ?? "",
            Environment.GetEnvironmentVariable("AZURE_BLOB_CONNECTION_STRING") ?? "",
            Environment.GetEnvironmentVariable("AZURE_BLOB_CONTAINER") ?? "");
    }

    public Dictionary<string, string?> ToConfigurationMap()
    {
        return new Dictionary<string, string?>
        {
            ["AzureCosmos:ConnectionString"] = CosmosConnectionString,
            ["AzureCosmos:DatabaseName"] = CosmosDatabaseName,
            ["AzureCosmos:DocumentsContainerName"] = CosmosDocumentsContainer,
            ["AzureCosmos:ChunksContainerName"] = CosmosChunksContainer,
            ["AzureStorage:BlobConnectionString"] = BlobConnectionString,
            ["AzureStorage:BlobContainerName"] = BlobContainerName
        };
    }
}
