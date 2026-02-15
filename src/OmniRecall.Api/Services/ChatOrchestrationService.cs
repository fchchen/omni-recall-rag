using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OmniRecall.Api.Contracts;

namespace OmniRecall.Api.Services;

public interface IChatOrchestrationService
{
    Task<ChatResponseDto> CompleteAsync(string prompt, int topK, CancellationToken cancellationToken = default);
}

public sealed partial class ChatOrchestrationService(
    IRecallSearchService recallSearchService,
    AiChatRouter chatRouter,
    IOptions<ChatQualityOptions> qualityOptions) : IChatOrchestrationService
{
    public async Task<ChatResponseDto> CompleteAsync(string prompt, int topK, CancellationToken cancellationToken = default)
    {
        var recall = await recallSearchService.SearchAsync(prompt, topK, cancellationToken);
        var options = qualityOptions.Value;

        if (!HasSufficientEvidence(recall.Citations, options))
        {
            return new ChatResponseDto(
                options.InsufficientEvidenceMessage,
                "guard",
                "insufficient-evidence",
                recall.Citations);
        }

        var groundedPrompt = BuildGroundedPrompt(prompt, recall.Citations);

        AiChatResponse response;
        try
        {
            response = await chatRouter.CompleteAsync(new AiChatRequest(groundedPrompt), cancellationToken);
        }
        catch (AiProviderUnavailableException) when (options.EnableRecallOnlyFallbackOnProviderFailure)
        {
            var fallbackAnswer = BuildRecallOnlyFallbackAnswer(recall.Citations, options);
            return new ChatResponseDto(
                fallbackAnswer,
                "recall-only",
                "free-tier-fallback",
                recall.Citations);
        }

        var postProcessed = PostProcessAnswer(response.Text, recall.Citations);

        return new ChatResponseDto(
            postProcessed.Answer,
            response.Provider,
            response.Model,
            postProcessed.Citations);
    }

    internal static bool HasSufficientEvidence(IReadOnlyList<RecallCitationDto> citations, ChatQualityOptions options)
    {
        if (citations.Count < Math.Max(1, options.MinimumCitationCount))
            return false;

        var threshold = Math.Max(0d, options.MinimumStrongCitationScore);
        return citations.Any(c => c.Score >= threshold);
    }

    internal static string BuildGroundedPrompt(string userQuestion, IReadOnlyList<RecallCitationDto> citations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an assistant that answers using the provided context snippets.");
        sb.AppendLine("The snippets can be partial excerpts from larger documents.");
        sb.AppendLine("If the user asks for improvements, critique, rewrite ideas, or optimization advice, provide actionable suggestions grounded in the snippet content.");
        sb.AppendLine("Only say you do not know when the snippets are clearly unrelated to the question.");
        sb.AppendLine();
        sb.AppendLine("Context:");
        if (citations.Count == 0)
        {
            sb.AppendLine("[no context]");
        }
        else
        {
            for (var i = 0; i < citations.Count; i++)
            {
                var c = citations[i];
                sb.AppendLine($"[{i + 1}] file={c.FileName} chunk={c.ChunkIndex} score={c.Score:F4}");
                sb.AppendLine(c.Snippet);
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Question: {userQuestion}");
        sb.AppendLine("Answer concisely and cite snippet numbers like [1], [2] when used.");
        sb.AppendLine("When giving advice, include concrete changes and examples based on the snippets.");
        return sb.ToString();
    }

    internal static (string Answer, IReadOnlyList<RecallCitationDto> Citations) PostProcessAnswer(
        string answer,
        IReadOnlyList<RecallCitationDto> citations)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return (string.Empty, []);
        if (citations.Count == 0)
            return (answer.Trim(), []);

        var referenced = new List<int>();
        var normalized = CitationMarkerRegex().Replace(answer, m =>
        {
            if (!int.TryParse(m.Groups[1].Value, out var n))
                return string.Empty;
            if (n < 1 || n > citations.Count)
                return string.Empty;

            referenced.Add(n);
            return $"[{n}]";
        });

        // Preserve paragraph breaks while normalizing extra horizontal spacing.
        var collapsed = HorizontalWhitespaceRegex().Replace(normalized, " ");
        collapsed = ExcessNewLinesRegex().Replace(collapsed, "\n\n").Trim();
        var uniqueReferenced = referenced
            .Distinct()
            .Select(n => citations[n - 1])
            .ToList();

        if (uniqueReferenced.Count == 0)
            return (collapsed, citations);

        return (collapsed, uniqueReferenced);
    }

    internal static string BuildRecallOnlyFallbackAnswer(
        IReadOnlyList<RecallCitationDto> citations,
        ChatQualityOptions options)
    {
        var max = Math.Max(1, options.RecallOnlyFallbackMaxCitations);
        var selected = citations.Take(max).ToList();
        if (selected.Count == 0)
            return options.RecallOnlyFallbackMessage;

        var sb = new StringBuilder();
        sb.AppendLine(options.RecallOnlyFallbackMessage);
        sb.AppendLine();
        sb.AppendLine("Top retrieved evidence:");
        for (var i = 0; i < selected.Count; i++)
        {
            var c = selected[i];
            sb.AppendLine($"[{i + 1}] {c.FileName} (chunk {c.ChunkIndex}, score {c.Score:F3})");
            sb.AppendLine(c.Snippet);
            if (i < selected.Count - 1)
                sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex CitationMarkerRegex();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessNewLinesRegex();
}
