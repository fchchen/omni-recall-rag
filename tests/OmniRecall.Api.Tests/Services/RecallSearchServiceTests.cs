using OmniRecall.Api.Data.Models;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Services;

public class RecallSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_WithEmbeddings_ReturnsMostSimilarChunkFirst()
    {
        var store = new InMemoryIngestionStore();
        await SeedAsync(store);
        var embeddingClient = new StubQueryEmbeddingClient([1f, 0f]);
        var sut = new RecallSearchService(store, embeddingClient);

        var result = await sut.SearchAsync("azure", 3);

        Assert.NotEmpty(result.Citations);
        Assert.Equal("doc-1", result.Citations[0].DocumentId);
        Assert.Equal("notes-azure.md", result.Citations[0].FileName);
    }

    [Fact]
    public async Task SearchAsync_NoQueryEmbedding_FallsBackToKeywordScore()
    {
        var store = new InMemoryIngestionStore();
        await SeedAsync(store);
        var embeddingClient = new StubQueryEmbeddingClient([]);
        var sut = new RecallSearchService(store, embeddingClient);

        var result = await sut.SearchAsync("kubernetes", 3);

        Assert.NotEmpty(result.Citations);
        Assert.Equal("doc-2", result.Citations[0].DocumentId);
    }

    [Fact]
    public async Task SearchAsync_StopWordsDoNotDiluteKeywordMatch()
    {
        var store = new InMemoryIngestionStore();
        await SeedAsync(store);
        var embeddingClient = new StubQueryEmbeddingClient([]);
        var sut = new RecallSearchService(store, embeddingClient);

        var result = await sut.SearchAsync("what is the kubernetes", 3);

        Assert.NotEmpty(result.Citations);
        Assert.Equal("doc-2", result.Citations[0].DocumentId);
    }

    private static async Task SeedAsync(InMemoryIngestionStore store)
    {
        var now = DateTime.UtcNow;

        await store.UpsertDocumentAsync(new CosmosDocumentRecord
        {
            Id = "doc-1",
            FileName = "notes-azure.md",
            SourceType = "file",
            BlobPath = "raw/doc1.md",
            ContentHash = "a1",
            ChunkCount = 1,
            CreatedAtUtc = now
        });

        await store.UpsertDocumentAsync(new CosmosDocumentRecord
        {
            Id = "doc-2",
            FileName = "notes-devops.md",
            SourceType = "file",
            BlobPath = "raw/doc2.md",
            ContentHash = "b1",
            ChunkCount = 1,
            CreatedAtUtc = now
        });

        await store.UpsertDocumentAsync(new CosmosDocumentRecord
        {
            Id = "doc-3",
            FileName = "notes-common.md",
            SourceType = "file",
            BlobPath = "raw/doc3.md",
            ContentHash = "c1",
            ChunkCount = 1,
            CreatedAtUtc = now
        });

        await store.UpsertChunksAsync([
            new CosmosChunkRecord
            {
                Id = "doc-1:0000",
                DocumentId = "doc-1",
                ChunkIndex = 0,
                Content = "azure cosmos db vector search",
                Embedding = [1f, 0f],
                CreatedAtUtc = now
            },
            new CosmosChunkRecord
            {
                Id = "doc-2:0000",
                DocumentId = "doc-2",
                ChunkIndex = 0,
                Content = "kubernetes deployment yaml and helm chart",
                Embedding = [0f, 1f],
                CreatedAtUtc = now
            },
            new CosmosChunkRecord
            {
                Id = "doc-3:0000",
                DocumentId = "doc-3",
                ChunkIndex = 0,
                Content = "what is the and of for",
                Embedding = [0f, 0f],
                CreatedAtUtc = now
            }
        ]);
    }
}

internal sealed class StubQueryEmbeddingClient(IReadOnlyList<float> vector) : IEmbeddingClient
{
    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var status = vector.Count > 0 ? EmbeddingStatus.Success : EmbeddingStatus.Empty;
        return Task.FromResult(new EmbeddingResult(vector, status, "stub"));
    }
}
