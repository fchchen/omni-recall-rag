namespace OmniRecall.Api.Contracts;

public sealed record UploadDocumentResponseDto(
    string DocumentId,
    string FileName,
    string SourceType,
    string BlobPath,
    int ChunkCount,
    string ContentHash,
    DateTime CreatedAtUtc);

public sealed record DocumentDetailsDto(
    string DocumentId,
    string FileName,
    string SourceType,
    string BlobPath,
    int ChunkCount,
    string ContentHash,
    DateTime CreatedAtUtc);

public sealed record DocumentListItemDto(
    string DocumentId,
    string FileName,
    string SourceType,
    int ChunkCount,
    DateTime CreatedAtUtc);

public sealed record DocumentChunkPreviewDto(
    string ChunkId,
    int ChunkIndex,
    string Snippet,
    bool HasEmbedding,
    DateTime CreatedAtUtc);

public sealed record ReindexDocumentResponseDto(
    string DocumentId,
    int ChunkCount,
    int EmbeddedCount,
    int RateLimitedCount,
    int EmptyCount,
    int FailedCount,
    DateTime ReindexedAtUtc);
