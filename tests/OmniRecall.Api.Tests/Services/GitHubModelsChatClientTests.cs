using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using OmniRecall.Api.Contracts;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Services;

public class GitHubModelsChatClientTests
{
    [Fact]
    public async Task CompleteAsync_ContentArray_ReturnsConcatenatedText()
    {
        var handler = new GitHubSequenceHttpHandler(
        [
            (HttpStatusCode.OK, """{"choices":[{"message":{"content":[{"type":"text","text":"hello "},{"type":"text","text":"world"}]}}]}""")
        ]);
        var sut = new GitHubModelsChatClient(new HttpClient(handler), BuildConfig());

        var result = await sut.CompleteAsync(new AiChatRequest("test"));

        Assert.Equal("hello world", result.Text);
        Assert.Equal("github-models", result.Provider);
    }

    [Fact]
    public async Task CompleteAsync_MissingContent_ThrowsMeaningfulError()
    {
        var handler = new GitHubSequenceHttpHandler(
        [
            (HttpStatusCode.OK, """{"choices":[{"message":{"role":"assistant"}}]}""")
        ]);
        var sut = new GitHubModelsChatClient(new HttpClient(handler), BuildConfig());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CompleteAsync(new AiChatRequest("test")));

        Assert.Contains("did not contain chat text", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("choices", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IConfiguration BuildConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHubModels:Token"] = "token",
                ["GitHubModels:Model"] = "deepseek/DeepSeek-V3-0324",
                ["GitHubModels:BaseUrl"] = "https://models.github.ai/inference"
            })
            .Build();
    }
}

internal sealed class GitHubSequenceHttpHandler(
    IReadOnlyList<(HttpStatusCode StatusCode, string Body)> responses) : HttpMessageHandler
{
    private int _index;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var current = responses[Math.Min(_index, responses.Count - 1)];
        _index++;

        return Task.FromResult(new HttpResponseMessage
        {
            StatusCode = current.StatusCode,
            Content = new StringContent(current.Body, Encoding.UTF8, "application/json")
        });
    }
}
