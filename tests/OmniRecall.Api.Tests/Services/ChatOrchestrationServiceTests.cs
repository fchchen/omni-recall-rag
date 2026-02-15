using OmniRecall.Api.Contracts;
using OmniRecall.Api.Services;
using Microsoft.Extensions.Options;

namespace OmniRecall.Api.Tests.Services;

public class ChatOrchestrationServiceTests
{
    [Fact]
    public async Task CompleteAsync_IncludesRecallContextAndReturnsCitations()
    {
        var recall = new StubRecallSearchService(new RecallSearchResponseDto(
            "what did I decide",
            [
                new RecallCitationDto(
                    "doc-1",
                    "decision-log.md",
                    "doc-1:0000",
                    0,
                    "Decided to use Azure Functions for API layer.",
                    0.91d,
                    DateTime.UtcNow)
            ]));

        var primary = ScriptedChatClient.WithSteps("primary", new AiChatResponse("Grounded answer", "gemini", "gemini"));
        var fallback = ScriptedChatClient.WithSteps("fallback", new AiChatResponse("Fallback", "deepseek", "github-models"));
        var router = new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });
        var sut = new ChatOrchestrationService(recall, router, Options.Create(new ChatQualityOptions()));

        var result = await sut.CompleteAsync("What did I decide for the API layer?", 3);

        Assert.Equal("Grounded answer", result.Answer);
        Assert.Single(result.Citations);
        Assert.Equal("decision-log.md", result.Citations[0].FileName);

        Assert.NotNull(recall.LastQuery);
        Assert.Contains("API layer", recall.LastQuery!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Context", primary.LastPrompt ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("decision-log.md", primary.LastPrompt ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("improvements, critique", primary.LastPrompt ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_StripsInvalidMarkers_AndFiltersCitationsToReferencedOnly()
    {
        var now = DateTime.UtcNow;
        var recall = new StubRecallSearchService(new RecallSearchResponseDto(
            "question",
            [
                new RecallCitationDto("doc-1", "a.md", "doc-1:0000", 0, "alpha", 0.9d, now),
                new RecallCitationDto("doc-2", "b.md", "doc-2:0000", 0, "beta", 0.8d, now)
            ]));

        var primary = ScriptedChatClient.WithSteps(
            "primary",
            new AiChatResponse("We chose Azure Functions [1] and skipped old stack [9].", "gemini", "gemini"));
        var fallback = ScriptedChatClient.WithSteps("fallback", new AiChatResponse("fallback", "deepseek", "github-models"));
        var router = new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });
        var sut = new ChatOrchestrationService(recall, router, Options.Create(new ChatQualityOptions()));

        var result = await sut.CompleteAsync("What did we choose?", 5);

        Assert.Equal("We chose Azure Functions [1] and skipped old stack .", result.Answer);
        Assert.Single(result.Citations);
        Assert.Equal("a.md", result.Citations[0].FileName);
    }

    [Fact]
    public async Task CompleteAsync_PreservesParagraphBreaks_InAnswer()
    {
        var now = DateTime.UtcNow;
        var recall = new StubRecallSearchService(new RecallSearchResponseDto(
            "question",
            [
                new RecallCitationDto("doc-1", "a.md", "doc-1:0000", 0, "alpha", 0.9d, now)
            ]));

        var answer = """
            Improvement 1 [1]

            Rewrite:
            - stronger opening
            - quantified impact
        """;
        var primary = ScriptedChatClient.WithSteps("primary", new AiChatResponse(answer, "gemini", "gemini"));
        var fallback = ScriptedChatClient.WithSteps("fallback", new AiChatResponse("fallback", "deepseek", "github-models"));
        var router = new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });
        var sut = new ChatOrchestrationService(recall, router, Options.Create(new ChatQualityOptions()));

        var result = await sut.CompleteAsync("format test", 5);

        Assert.Contains("\n\n", result.Answer);
        Assert.Contains("Rewrite:", result.Answer);
        Assert.Contains("- stronger opening", result.Answer);
    }

    [Fact]
    public async Task CompleteAsync_InsufficientEvidence_ReturnsGuardMessage_WithoutLlmCall()
    {
        var now = DateTime.UtcNow;
        var recall = new StubRecallSearchService(new RecallSearchResponseDto(
            "question",
            [
                new RecallCitationDto("doc-1", "weak.md", "doc-1:0000", 0, "weak context", 0.05d, now)
            ]));

        var primary = ScriptedChatClient.WithSteps("primary", new AiChatResponse("should not be used", "gemini", "gemini"));
        var fallback = ScriptedChatClient.WithSteps("fallback", new AiChatResponse("fallback", "deepseek", "github-models"));
        var router = new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });
        var options = Options.Create(new ChatQualityOptions
        {
            MinimumCitationCount = 1,
            MinimumStrongCitationScore = 0.2d,
            InsufficientEvidenceMessage = "Insufficient evidence."
        });
        var sut = new ChatOrchestrationService(recall, router, options);

        var result = await sut.CompleteAsync("Question", 5);

        Assert.Equal("Insufficient evidence.", result.Answer);
        Assert.Equal("guard", result.Provider);
        Assert.Equal("insufficient-evidence", result.Model);
        Assert.Equal(0, primary.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_InsufficientEvidence_WhenCitationCountBelowMinimum()
    {
        var now = DateTime.UtcNow;
        var recall = new StubRecallSearchService(new RecallSearchResponseDto(
            "question",
            [
                new RecallCitationDto("doc-1", "single.md", "doc-1:0000", 0, "single snippet", 0.88d, now)
            ]));

        var primary = ScriptedChatClient.WithSteps("primary", new AiChatResponse("should not be used", "gemini", "gemini"));
        var fallback = ScriptedChatClient.WithSteps("fallback", new AiChatResponse("fallback", "deepseek", "github-models"));
        var router = new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });
        var options = Options.Create(new ChatQualityOptions
        {
            MinimumCitationCount = 2,
            MinimumStrongCitationScore = 0.2d,
            InsufficientEvidenceMessage = "Not enough citations."
        });
        var sut = new ChatOrchestrationService(recall, router, options);

        var result = await sut.CompleteAsync("Question", 5);

        Assert.Equal("Not enough citations.", result.Answer);
        Assert.Equal("guard", result.Provider);
        Assert.Equal(0, primary.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_Proceeds_WhenOneCitationMeetsStrongScoreThreshold()
    {
        var now = DateTime.UtcNow;
        var recall = new StubRecallSearchService(new RecallSearchResponseDto(
            "question",
            [
                new RecallCitationDto("doc-1", "weak.md", "doc-1:0000", 0, "weak snippet", 0.1d, now),
                new RecallCitationDto("doc-2", "strong.md", "doc-2:0000", 0, "strong snippet", 0.55d, now)
            ]));

        var primary = ScriptedChatClient.WithSteps("primary", new AiChatResponse("Grounded answer [2]", "gemini", "gemini"));
        var fallback = ScriptedChatClient.WithSteps("fallback", new AiChatResponse("fallback", "deepseek", "github-models"));
        var router = new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });
        var options = Options.Create(new ChatQualityOptions
        {
            MinimumCitationCount = 2,
            MinimumStrongCitationScore = 0.5d
        });
        var sut = new ChatOrchestrationService(recall, router, options);

        var result = await sut.CompleteAsync("Question", 5);

        Assert.Equal("Grounded answer [2]", result.Answer);
        Assert.Single(result.Citations);
        Assert.Equal("strong.md", result.Citations[0].FileName);
        Assert.Equal(1, primary.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_WhenProvidersUnavailable_AndFreeTierFallbackEnabled_ReturnsRecallOnlyAnswer()
    {
        var now = DateTime.UtcNow;
        var recall = new StubRecallSearchService(new RecallSearchResponseDto(
            "question",
            [
                new RecallCitationDto("doc-1", "a.md", "doc-1:0000", 0, "alpha snippet", 0.91d, now),
                new RecallCitationDto("doc-2", "b.md", "doc-2:0001", 1, "beta snippet", 0.82d, now),
                new RecallCitationDto("doc-3", "c.md", "doc-3:0002", 2, "gamma snippet", 0.80d, now)
            ]));

        var primary = ScriptedChatClient.WithSteps("primary", new AiRateLimitException("429"), new TimeoutException("timeout"));
        var fallback = ScriptedChatClient.WithSteps("fallback", new HttpRequestException("network"), new AiRateLimitException("429"));
        var router = new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });
        var options = Options.Create(new ChatQualityOptions
        {
            MinimumCitationCount = 1,
            MinimumStrongCitationScore = 0.1d,
            EnableRecallOnlyFallbackOnProviderFailure = true,
            RecallOnlyFallbackMaxCitations = 2,
            RecallOnlyFallbackMessage = "Free-tier fallback."
        });
        var sut = new ChatOrchestrationService(recall, router, options);

        var result = await sut.CompleteAsync("Question", 5);

        Assert.Equal("recall-only", result.Provider);
        Assert.Equal("free-tier-fallback", result.Model);
        Assert.Contains("Free-tier fallback.", result.Answer);
        Assert.Contains("[1] a.md", result.Answer);
        Assert.Contains("[2] b.md", result.Answer);
        Assert.DoesNotContain("c.md", result.Answer);
        Assert.Equal(3, result.Citations.Count);
    }

    [Fact]
    public async Task CompleteAsync_WhenProvidersUnavailable_AndFallbackDisabled_Throws()
    {
        var now = DateTime.UtcNow;
        var recall = new StubRecallSearchService(new RecallSearchResponseDto(
            "question",
            [
                new RecallCitationDto("doc-1", "a.md", "doc-1:0000", 0, "alpha snippet", 0.91d, now)
            ]));

        var primary = ScriptedChatClient.WithSteps("primary", new AiRateLimitException("429"), new TimeoutException("timeout"));
        var fallback = ScriptedChatClient.WithSteps("fallback", new HttpRequestException("network"), new AiRateLimitException("429"));
        var router = new AiChatRouter(primary, fallback, new AiRoutingOptions { MaxAttemptsPerProvider = 2, RetryBaseDelayMs = 0 });
        var options = Options.Create(new ChatQualityOptions
        {
            MinimumCitationCount = 1,
            MinimumStrongCitationScore = 0.1d,
            EnableRecallOnlyFallbackOnProviderFailure = false
        });
        var sut = new ChatOrchestrationService(recall, router, options);

        await Assert.ThrowsAsync<AiProviderUnavailableException>(() => sut.CompleteAsync("Question", 5));
    }
}

internal sealed class StubRecallSearchService(RecallSearchResponseDto response) : IRecallSearchService
{
    public string? LastQuery { get; private set; }

    public Task<RecallSearchResponseDto> SearchAsync(string query, int topK, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        return Task.FromResult(response);
    }
}
