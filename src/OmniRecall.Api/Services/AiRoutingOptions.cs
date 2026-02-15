namespace OmniRecall.Api.Services;

public sealed class AiRoutingOptions
{
    public int MaxAttemptsPerProvider { get; init; } = 2;
    public int RetryBaseDelayMs { get; init; } = 500;
    public int RetryMaxDelayMs { get; init; } = 5_000;
}
