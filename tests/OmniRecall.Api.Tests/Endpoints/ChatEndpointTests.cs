using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Mvc;
using OmniRecall.Api.Contracts;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Endpoints;

public class ChatEndpointTests
{
    [Fact]
    public async Task PostChat_EmptyPrompt_ReturnsBadRequest()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequestDto(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostChat_WithValidPromptAndNoEvidence_ReturnsGuardResponse()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<AiChatRouter>();
                    services.AddScoped(_ =>
                    {
                        var primary = StubAiChatClient.WithResponses(
                            "gemini",
                            new AiChatResponse("primary answer", "gemini-2.5-flash", "gemini"));
                        var fallback = StubAiChatClient.WithResponses(
                            "github-models",
                            new AiChatResponse("fallback answer", "deepseek/DeepSeek-V3-0324", "github-models"));
                        return new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });
                    });
                });
            });

        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequestDto("What did I decide yesterday?"));
        var body = await response.Content.ReadFromJsonAsync<ChatResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Contains("Insufficient evidence", body!.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("guard", body.Provider);
        Assert.Equal("insufficient-evidence", body.Model);
        Assert.Empty(body.Citations);
    }

    [Fact]
    public async Task PostChat_AfterUpload_ReturnsCitations()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<AiChatRouter>();
                    services.RemoveAll<IEmbeddingClient>();
                    services.AddSingleton<IEmbeddingClient>(new DeterministicEmbeddingClient([0.2f, 0.8f, 0.4f]));
                    services.AddScoped(_ =>
                    {
                        var primary = StubAiChatClient.WithResponses(
                            "gemini",
                            new AiChatResponse("grounded answer", "gemini-2.5-flash", "gemini"));
                        var fallback = StubAiChatClient.WithResponses(
                            "github-models",
                            new AiChatResponse("fallback answer", "deepseek/DeepSeek-V3-0324", "github-models"));
                        return new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });
                    });
                });
            });

        var client = factory.CreateClient();

        var form = new MultipartFormDataContent();
        var text = "we decided to use azure functions for backend api";
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(text)), "file", "decision-log.md");
        var uploadResponse = await client.PostAsync("/api/documents/upload", form);
        uploadResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequestDto("What backend did we choose?"));
        var body = await response.Content.ReadFromJsonAsync<ChatResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("grounded answer", body!.Answer);
        Assert.NotEmpty(body.Citations);
        Assert.Equal("decision-log.md", body.Citations[0].FileName);
    }

    [Fact]
    public async Task PostChat_UnhandledException_ReturnsGlobalProblemResponse()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IChatOrchestrationService>();
                    services.AddScoped<IChatOrchestrationService>(_ => new ThrowingChatOrchestrationService());
                });
            });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequestDto("trigger error"));
        var body = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("Unexpected server error", body!.Title);
    }
}

internal sealed class StubAiChatClient : IAiChatClient
{
    private readonly Queue<AiChatResponse> _responses = new();

    public string ProviderName { get; }

    private StubAiChatClient(string providerName, IEnumerable<AiChatResponse> responses)
    {
        ProviderName = providerName;
        foreach (var response in responses)
        {
            _responses.Enqueue(response);
        }
    }

    public static StubAiChatClient WithResponses(string providerName, params AiChatResponse[] responses)
    {
        return new StubAiChatClient(providerName, responses);
    }

    public Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        if (_responses.Count == 0)
            throw new InvalidOperationException("No stub response configured.");

        return Task.FromResult(_responses.Dequeue());
    }
}

internal sealed class ThrowingChatOrchestrationService : IChatOrchestrationService
{
    public Task<ChatResponseDto> CompleteAsync(string prompt, int topK, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("boom");
    }
}

internal sealed class DeterministicEmbeddingClient(IReadOnlyList<float> vector) : IEmbeddingClient
{
    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EmbeddingResult(vector, EmbeddingStatus.Success, "test"));
    }
}
