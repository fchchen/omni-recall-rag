namespace OmniRecall.Api.Contracts;

public sealed record HealthDependencyDto(
    string Name,
    string Status,
    string Detail,
    long DurationMs);

public sealed record HealthResponseDto(
    string Status,
    DateTime TimestampUtc,
    IReadOnlyList<HealthDependencyDto> Dependencies);
