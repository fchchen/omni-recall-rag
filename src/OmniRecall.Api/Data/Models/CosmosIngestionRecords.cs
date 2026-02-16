using System.Text.Json.Serialization;

namespace OmniRecall.Api.Data.Models;

public sealed class CosmosDocumentRecord
{
    public string Id { get; init; } = string.Empty;
    [JsonPropertyName("PartitionKey")]
    public string PartitionKey { get; init; } = "user:default";
    public string Type { get; init; } = "document";
    public string FileName { get; init; } = string.Empty;
    public string SourceType { get; init; } = "file";
    public string BlobPath { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public int ChunkCount { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class CosmosChunkRecord
{
    public string Id { get; init; } = string.Empty;
    [JsonPropertyName("PartitionKey")]
    public string PartitionKey { get; init; } = "user:default";
    public string Type { get; init; } = "chunk";
    public string DocumentId { get; init; } = string.Empty;
    public int ChunkIndex { get; init; }
    public string Content { get; init; } = string.Empty;
    public IReadOnlyList<float>? Embedding { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
