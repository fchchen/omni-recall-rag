using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Services;

public class GeminiEmbeddingClientTests
{
    [Fact]
    public async Task EmbedAsync_Success_ReturnsEmbeddingValues()
    {
        var handler = new StubHttpMessageHandler(
            """{"embedding":{"values":[0.11,0.22,0.33]}}""",
            HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var config = BuildConfig("test-key");
        var sut = new GeminiEmbeddingClient(httpClient, config, NullLogger<GeminiEmbeddingClient>.Instance);

        var result = await sut.EmbedAsync("hello world");

        Assert.Equal(EmbeddingStatus.Success, result.Status);
        Assert.Equal(3, result.Vector.Count);
        Assert.Equal(0.11f, result.Vector[0]);
        Assert.Equal(0.22f, result.Vector[1]);
        Assert.Equal(0.33f, result.Vector[2]);
    }

    [Fact]
    public async Task EmbedAsync_RateLimited_ReturnsEmpty()
    {
        var handler = new StubHttpMessageHandler("{}", HttpStatusCode.TooManyRequests);
        var httpClient = new HttpClient(handler);
        var config = BuildConfig("test-key");
        var sut = new GeminiEmbeddingClient(httpClient, config, NullLogger<GeminiEmbeddingClient>.Instance);

        var result = await sut.EmbedAsync("hello world");

        Assert.Equal(EmbeddingStatus.RateLimited, result.Status);
        Assert.Empty(result.Vector);
    }

    [Fact]
    public async Task EmbedAsync_NoApiKey_ReturnsEmpty()
    {
        var handler = new StubHttpMessageHandler("{}", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var config = BuildConfig(apiKey: "");
        var sut = new GeminiEmbeddingClient(httpClient, config, NullLogger<GeminiEmbeddingClient>.Instance);

        var result = await sut.EmbedAsync("hello world");

        Assert.Equal(EmbeddingStatus.Empty, result.Status);
        Assert.Empty(result.Vector);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task EmbedAsync_ConfiguredModelNotFound_FallsBackToSupportedModel()
    {
        var handler = new SequentialStubHttpMessageHandler(
            [
                ("{}", HttpStatusCode.NotFound),
                ("""{"embedding":{"values":[0.9,0.8]}}""", HttpStatusCode.OK)
            ]);
        var httpClient = new HttpClient(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "test-key",
                ["Gemini:BaseUrl"] = "https://generativelanguage.googleapis.com/v1beta",
                ["Gemini:EmbeddingModel"] = "text-embedding-004"
            })
            .Build();
        var sut = new GeminiEmbeddingClient(httpClient, config, NullLogger<GeminiEmbeddingClient>.Instance);

        var result = await sut.EmbedAsync("hello world");

        Assert.Equal(EmbeddingStatus.Success, result.Status);
        Assert.Equal(2, result.Vector.Count);
        Assert.Equal(2, handler.RequestCount);
    }

    private static IConfiguration BuildConfig(string apiKey)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = apiKey,
                ["Gemini:BaseUrl"] = "https://generativelanguage.googleapis.com/v1beta",
                ["Gemini:EmbeddingModel"] = "gemini-embedding-001"
            })
            .Build();
    }
}

internal sealed class StubHttpMessageHandler(string responseBody, HttpStatusCode statusCode) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage
        {
            StatusCode = statusCode,
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });
    }
}

internal sealed class SequentialStubHttpMessageHandler(
    IReadOnlyList<(string Body, HttpStatusCode StatusCode)> responses) : HttpMessageHandler
{
    private int _index;
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        var i = Math.Min(_index, responses.Count - 1);
        var response = responses[i];
        _index++;

        return Task.FromResult(new HttpResponseMessage
        {
            StatusCode = response.StatusCode,
            Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
        });
    }
}
