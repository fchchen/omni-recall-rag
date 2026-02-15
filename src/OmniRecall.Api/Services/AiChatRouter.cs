using OmniRecall.Api.Contracts;

namespace OmniRecall.Api.Services;

public sealed class AiChatRouter
{
    private readonly IAiChatClient _primary;
    private readonly IAiChatClient _fallback;
    private readonly AiRoutingOptions _options;
    private readonly ILogger<AiChatRouter>? _logger;

    public AiChatRouter(
        IAiChatClient primary,
        IAiChatClient fallback,
        AiRoutingOptions? options = null,
        ILogger<AiChatRouter>? logger = null)
    {
        _primary = primary;
        _fallback = fallback;
        _options = options ?? new AiRoutingOptions();
        _logger = logger;
    }

    public async Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        var primaryResult = await TryProviderAsync(_primary, request, cancellationToken);
        if (primaryResult.Success && primaryResult.Response is not null)
            return primaryResult.Response;

        _logger?.LogWarning(
            "Primary provider {Provider} failed after retries. Falling back to {Fallback}.",
            _primary.ProviderName,
            _fallback.ProviderName);

        var fallbackResult = await TryProviderAsync(_fallback, request, cancellationToken);
        if (fallbackResult.Success && fallbackResult.Response is not null)
            return fallbackResult.Response;

        throw new AiProviderUnavailableException(
            $"Both AI providers failed: primary={_primary.ProviderName}, fallback={_fallback.ProviderName}",
            primaryResult.Exception,
            fallbackResult.Exception);
    }

    private async Task<ProviderAttemptResult> TryProviderAsync(
        IAiChatClient client,
        AiChatRequest request,
        CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, _options.MaxAttemptsPerProvider);
        Exception? lastException = null;

        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                var response = await client.CompleteAsync(request, cancellationToken);
                return ProviderAttemptResult.FromSuccess(response);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                lastException = ex;
                _logger?.LogWarning(
                    ex,
                    "Transient failure from provider {Provider} on attempt {Attempt}/{TotalAttempts}.",
                    client.ProviderName,
                    i,
                    attempts);

                if (i < attempts)
                {
                    var delay = ComputeBackoffDelay(i, _options);
                    if (delay > TimeSpan.Zero)
                    {
                        _logger?.LogInformation(
                            "Waiting {DelayMs}ms before retrying provider {Provider}.",
                            (int)delay.TotalMilliseconds,
                            client.ProviderName);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Non-transient failure from provider {Provider}.",
                    client.ProviderName);
                return ProviderAttemptResult.FromFailure(ex);
            }
        }

        return ProviderAttemptResult.FromFailure(lastException ?? new InvalidOperationException("Unknown provider failure."));
    }

    private static bool IsTransient(Exception ex)
    {
        return ex is AiRateLimitException or TimeoutException or HttpRequestException;
    }

    private static TimeSpan ComputeBackoffDelay(int attemptNumber, AiRoutingOptions options)
    {
        var baseMs = Math.Max(0, options.RetryBaseDelayMs);
        if (baseMs == 0)
            return TimeSpan.Zero;

        var maxMs = Math.Max(baseMs, options.RetryMaxDelayMs);
        var power = Math.Max(0, attemptNumber - 1);
        double delayMs;
        try
        {
            delayMs = checked(baseMs * Math.Pow(2, power));
        }
        catch (OverflowException)
        {
            delayMs = maxMs;
        }

        delayMs = Math.Min(delayMs, maxMs);
        return TimeSpan.FromMilliseconds(delayMs);
    }

    private sealed record ProviderAttemptResult(bool Success, AiChatResponse? Response, Exception? Exception)
    {
        public static ProviderAttemptResult FromSuccess(AiChatResponse response) => new(true, response, null);
        public static ProviderAttemptResult FromFailure(Exception ex) => new(false, null, ex);
    }
}

public sealed class AiRateLimitException(string message) : Exception(message);

public sealed class AiProviderUnavailableException : Exception
{
    public Exception? PrimaryException { get; }
    public Exception? FallbackException { get; }

    public AiProviderUnavailableException(string message, Exception? primaryException, Exception? fallbackException)
        : base(message, fallbackException ?? primaryException)
    {
        PrimaryException = primaryException;
        FallbackException = fallbackException;
    }
}
