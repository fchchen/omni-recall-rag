namespace OmniRecall.Api.Contracts;

public sealed record RecallSearchRequestDto(string Query, int TopK = 5);

public sealed record RecallCitationDto(
    string DocumentId,
    string FileName,
    string ChunkId,
    int ChunkIndex,
    string Snippet,
    double Score,
    DateTime CreatedAtUtc);

public sealed record RecallSearchResponseDto(
    string Query,
    IReadOnlyList<RecallCitationDto> Citations);
