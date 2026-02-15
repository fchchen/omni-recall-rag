namespace OmniRecall.Api.Contracts;

public sealed record ChatRequestDto(string Prompt, int TopK = 5);

public sealed record ChatResponseDto(
    string Answer,
    string Provider,
    string Model,
    IReadOnlyList<RecallCitationDto> Citations);
