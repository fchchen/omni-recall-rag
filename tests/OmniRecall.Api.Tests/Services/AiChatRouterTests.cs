using OmniRecall.Api.Contracts;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Services;

public class AiChatRouterTests
{
    [Fact]
    public async Task CompleteAsync_PrimarySuccess_DoesNotCallFallback()
    {
        var primary = ScriptedChatClient.WithSteps("primary", new AiChatResponse("ok", "gemini-fast", "primary"));
        var fallback = ScriptedChatClient.WithSteps("fallback", new AiChatResponse("fallback", "deepseek-v3", "fallback"));
        var sut = new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });

        var result = await sut.CompleteAsync(new AiChatRequest("hello"));

        Assert.Equal("ok", result.Text);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_PrimaryRateLimitedThenSuccess_RetriesPrimary()
    {
        var primary = ScriptedChatClient.WithSteps(
            "primary",
            new AiRateLimitException("429"),
            new AiChatResponse("recovered", "gemini-fast", "primary"));
        var fallback = ScriptedChatClient.WithSteps("fallback", new AiChatResponse("fallback", "deepseek-v3", "fallback"));
        var sut = new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });

        var result = await sut.CompleteAsync(new AiChatRequest("hello"));

        Assert.Equal("recovered", result.Text);
        Assert.Equal(2, primary.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_PrimaryExhausted_UsesFallback()
    {
        var primary = ScriptedChatClient.WithSteps(
            "primary",
            new AiRateLimitException("429"),
            new TimeoutException("timeout"));
        var fallback = ScriptedChatClient.WithSteps("fallback", new AiChatResponse("fallback-ok", "deepseek-v3", "fallback"));
        var sut = new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });

        var result = await sut.CompleteAsync(new AiChatRequest("hello"));

        Assert.Equal("fallback-ok", result.Text);
        Assert.Equal(2, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_AllProvidersFail_ThrowsProviderUnavailable()
    {
        var primary = ScriptedChatClient.WithSteps(
            "primary",
            new AiRateLimitException("429"),
            new TimeoutException("timeout"));
        var fallback = ScriptedChatClient.WithSteps(
            "fallback",
            new HttpRequestException("network"),
            new AiRateLimitException("429"));
        var sut = new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });

        var ex = await Assert.ThrowsAsync<AiProviderUnavailableException>(() =>
            sut.CompleteAsync(new AiChatRequest("hello")));

        Assert.Contains("primary", ex.Message);
        Assert.Contains("fallback", ex.Message);
        Assert.Equal(2, primary.CallCount);
        Assert.Equal(2, fallback.CallCount);
    }
}

internal sealed class ScriptedChatClient : IAiChatClient
{
    private readonly Queue<object> _steps = new();

    public string ProviderName { get; }
    public int CallCount { get; private set; }
    public string? LastPrompt { get; private set; }

    private ScriptedChatClient(string providerName, IEnumerable<object> steps)
    {
        ProviderName = providerName;
        foreach (var step in steps)
        {
            _steps.Enqueue(step);
        }
    }

    public static ScriptedChatClient WithSteps(string providerName, params object[] steps)
    {
        return new ScriptedChatClient(providerName, steps);
    }

    public Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastPrompt = request.Prompt;
        var step = _steps.Count > 0 ? _steps.Dequeue() : new InvalidOperationException("No scripted step configured.");

        return step switch
        {
            AiChatResponse response => Task.FromResult(response),
            Exception ex => Task.FromException<AiChatResponse>(ex),
            _ => Task.FromException<AiChatResponse>(new InvalidOperationException("Unsupported scripted step."))
        };
    }
}
