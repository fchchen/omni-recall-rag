using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniRecall.Api.Data.Models;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Services;

public class DocumentIngestionServiceTests
{
    [Fact]
    public async Task IngestAsync_PersistsDocumentAndChunks()
    {
        var options = Options.Create(new IngestionOptions
        {
            ChunkSizeWords = 6,
            ChunkOverlapWords = 2
        });
        var chunker = new SlidingWindowTextChunker();
        var store = new InMemoryIngestionStore();
        var rawStore = new FakeRawDocumentStore();
        var embeddingClient = new FakeEmbeddingClient();
        var sut = new DocumentIngestionService(
            chunker,
            store,
            rawStore,
            embeddingClient,
            options,
            NullLogger<DocumentIngestionService>.Instance);

        var content = string.Join(' ', Enumerable.Range(1, 15).Select(i => $"token{i}"));
        var result = await sut.IngestAsync("notes.txt", content, "file");

        Assert.False(string.IsNullOrWhiteSpace(result.DocumentId));
        Assert.True(result.ChunkCount >= 2);
        Assert.False(string.IsNullOrWhiteSpace(result.ContentHash));
        Assert.Equal("raw/notes.txt", result.BlobPath);
        Assert.Equal(result.ChunkCount, embeddingClient.RequestCount);

        var savedDoc = await store.GetDocumentAsync(result.DocumentId);
        Assert.NotNull(savedDoc);
        Assert.Equal(result.ChunkCount, savedDoc!.ChunkCount);
        Assert.Equal("raw/notes.txt", savedDoc.BlobPath);

        var savedChunks = await store.GetChunksByDocumentIdAsync(result.DocumentId);
        Assert.Equal(result.ChunkCount, savedChunks.Count);
        Assert.All(savedChunks, c => Assert.NotNull(c.Embedding));
    }

    [Fact]
    public async Task IngestAsync_DuplicateContentHash_ReturnsExistingDocument_WithoutReEmbedding()
    {
        var options = Options.Create(new IngestionOptions
        {
            ChunkSizeWords = 8,
            ChunkOverlapWords = 2
        });
        var chunker = new SlidingWindowTextChunker();
        var store = new InMemoryIngestionStore();
        var rawStore = new FakeRawDocumentStore();
        var embeddingClient = new FakeEmbeddingClient();
        var sut = new DocumentIngestionService(
            chunker,
            store,
            rawStore,
            embeddingClient,
            options,
            NullLogger<DocumentIngestionService>.Instance);

        var content = string.Join(' ', Enumerable.Range(1, 24).Select(i => $"token{i}"));
        var first = await sut.IngestAsync("first.md", content, "file");
        var firstEmbedCount = embeddingClient.RequestCount;
        var firstSaveCount = rawStore.SaveCount;

        var second = await sut.IngestAsync("second.md", content, "file");

        Assert.Equal(first.DocumentId, second.DocumentId);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(firstEmbedCount, embeddingClient.RequestCount);
        Assert.Equal(firstSaveCount, rawStore.SaveCount);
    }

    [Fact]
    public async Task IngestAsync_EmbedsChunksInParallel_RespectsConfiguredParallelism()
    {
        var options = Options.Create(new IngestionOptions
        {
            ChunkSizeWords = 4,
            ChunkOverlapWords = 1,
            EmbeddingParallelism = 3
        });
        var chunker = new SlidingWindowTextChunker();
        var store = new InMemoryIngestionStore();
        var rawStore = new FakeRawDocumentStore();
        var embeddingClient = new ConcurrencyTrackingEmbeddingClient(delayMs: 30);
        var sut = new DocumentIngestionService(
            chunker,
            store,
            rawStore,
            embeddingClient,
            options,
            NullLogger<DocumentIngestionService>.Instance);

        var content = string.Join(' ', Enumerable.Range(1, 60).Select(i => $"token{i}"));

        var result = await sut.IngestAsync("parallel.md", content, "file");

        Assert.True(result.ChunkCount > 3);
        Assert.True(embeddingClient.MaxConcurrency > 1);
        Assert.True(embeddingClient.MaxConcurrency <= 3);
    }
}

internal sealed class FakeRawDocumentStore : IRawDocumentStore
{
    public int SaveCount { get; private set; }

    public Task<string> SaveAsync(
        string fileName,
        string content,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult($"raw/{fileName}");
    }
}

internal sealed class FakeEmbeddingClient : IEmbeddingClient
{
    public int RequestCount { get; private set; }

    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        RequestCount++;
        return Task.FromResult(new EmbeddingResult([0.1f, 0.2f, 0.3f], EmbeddingStatus.Success, "fake"));
    }
}

internal sealed class ConcurrencyTrackingEmbeddingClient(int delayMs) : IEmbeddingClient
{
    private int _inFlight;
    private int _maxConcurrency;

    public int MaxConcurrency => _maxConcurrency;

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var nowInFlight = Interlocked.Increment(ref _inFlight);
        while (true)
        {
            var snapshot = _maxConcurrency;
            if (nowInFlight <= snapshot)
                break;

            if (Interlocked.CompareExchange(ref _maxConcurrency, nowInFlight, snapshot) == snapshot)
                break;
        }

        try
        {
            await Task.Delay(delayMs, cancellationToken);
            return new EmbeddingResult([0.1f, 0.2f, 0.3f], EmbeddingStatus.Success, "concurrent");
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}
