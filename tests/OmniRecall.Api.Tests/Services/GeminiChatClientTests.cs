using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OmniRecall.Api.Contracts;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Services;

public class GeminiChatClientTests
{
    [Fact]
    public async Task CompleteAsync_PrimaryRateLimited_FallsBackToSecondaryModel()
    {
        var handler = new SequenceChatHttpHandler(
        [
            (HttpStatusCode.TooManyRequests, "{}"),
            (HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"fallback ok"}]}}]}""")
        ]);
        var sut = new GeminiChatClient(
            new HttpClient(handler),
            BuildConfig(primaryModel: "gemini-2.5-flash", fallbackModels: ["gemini-2.5-pro"]),
            NullLogger<GeminiChatClient>.Instance);

        var result = await sut.CompleteAsync(new AiChatRequest("hello"));

        Assert.Equal("fallback ok", result.Text);
        Assert.Equal("gemini-2.5-pro", result.Model);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task CompleteAsync_PrimaryNotFound_FallsBackToSecondaryModel()
    {
        var handler = new SequenceChatHttpHandler(
        [
            (HttpStatusCode.NotFound, """{"error":{"message":"model not found"}}"""),
            (HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"second model"}]}}]}""")
        ]);
        var sut = new GeminiChatClient(
            new HttpClient(handler),
            BuildConfig(primaryModel: "gemini-2.5-flash", fallbackModels: ["gemini-2-flash"]),
            NullLogger<GeminiChatClient>.Instance);

        var result = await sut.CompleteAsync(new AiChatRequest("hello"));

        Assert.Equal("second model", result.Text);
        Assert.Equal("gemini-2-flash", result.Model);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task CompleteAsync_AllCandidateModelsRateLimited_ThrowsRateLimit()
    {
        var handler = new SequenceChatHttpHandler(
        [
            (HttpStatusCode.TooManyRequests, "{}"),
            (HttpStatusCode.TooManyRequests, "{}")
        ]);
        var sut = new GeminiChatClient(
            new HttpClient(handler),
            BuildConfig(primaryModel: "gemini-2.5-flash", fallbackModels: ["gemini-2.5-pro"]),
            NullLogger<GeminiChatClient>.Instance);

        await Assert.ThrowsAsync<AiRateLimitException>(() => sut.CompleteAsync(new AiChatRequest("hello")));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task CompleteAsync_ResponseWithoutText_ThrowsMeaningfulError()
    {
        var handler = new SequenceChatHttpHandler(
        [
            (HttpStatusCode.OK, """{"promptFeedback":{"blockReason":"SAFETY"}}""")
        ]);
        var sut = new GeminiChatClient(
            new HttpClient(handler),
            BuildConfig(primaryModel: "gemini-2.5-flash", fallbackModels: []),
            NullLogger<GeminiChatClient>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CompleteAsync(new AiChatRequest("hello")));

        Assert.Contains("did not contain chat text", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blockReason", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IConfiguration BuildConfig(string primaryModel, IReadOnlyList<string> fallbackModels)
    {
        var data = new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "test-key",
            ["Gemini:BaseUrl"] = "https://generativelanguage.googleapis.com/v1beta",
            ["Gemini:Model"] = primaryModel
        };

        for (var i = 0; i < fallbackModels.Count; i++)
        {
            data[$"Gemini:FallbackModels:{i}"] = fallbackModels[i];
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }
}

internal sealed class SequenceChatHttpHandler(
    IReadOnlyList<(HttpStatusCode StatusCode, string Body)> responses) : HttpMessageHandler
{
    private int _index;
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        var current = responses[Math.Min(_index, responses.Count - 1)];
        _index++;

        return Task.FromResult(new HttpResponseMessage
        {
            StatusCode = current.StatusCode,
            Content = new StringContent(current.Body, Encoding.UTF8, "application/json")
        });
    }
}
