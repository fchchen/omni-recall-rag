namespace OmniRecall.Api.Services;

public sealed class ChatQualityOptions
{
    public int MinimumCitationCount { get; init; } = 1;
    public double MinimumStrongCitationScore { get; init; } = 0.25d;
    public string InsufficientEvidenceMessage { get; init; } =
        "Insufficient evidence in current indexed snippets. Try uploading more relevant documents or increasing TopK.";
    public bool EnableRecallOnlyFallbackOnProviderFailure { get; init; } = false;
    public int RecallOnlyFallbackMaxCitations { get; init; } = 4;
    public string RecallOnlyFallbackMessage { get; init; } =
        "AI providers are temporarily unavailable on free tier. Returning retrieval-only answer from indexed snippets.";
}
