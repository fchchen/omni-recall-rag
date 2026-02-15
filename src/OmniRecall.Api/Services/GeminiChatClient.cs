using System.Net;
using System.Text;
using System.Text.Json;
using OmniRecall.Api.Contracts;

namespace OmniRecall.Api.Services;

public sealed class GeminiChatClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GeminiChatClient> logger) : IAiChatClient
{
    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string DefaultModel = "gemini-2.5-flash";
    private static readonly string[] DefaultFallbackModels =
    [
        "gemini-2.5-flash-lite",
        "gemini-flash-latest",
        "gemini-flash-lite-latest",
        "gemini-3-flash-preview"
    ];

    public string ProviderName => "gemini";

    public async Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key not configured.");

        var baseUrl = configuration["Gemini:BaseUrl"] ?? DefaultBaseUrl;
        var models = ResolveCandidateModels();
        Exception? lastException = null;

        foreach (var model in models)
        {
            var url = $"{baseUrl}/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            var payload = JsonSerializer.Serialize(new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = request.Prompt }
                        }
                    }
                }
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(url, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                lastException = new AiRateLimitException($"Gemini model '{model}' rate limited.");
                logger.LogWarning("Gemini model {Model} was rate limited. Trying next model if available.", model);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var canFailover = CanFailoverToNextModel(response.StatusCode, body);
                var message = $"Gemini API returned {response.StatusCode} for model '{model}': {body}";
                lastException = new HttpRequestException(message);

                if (canFailover)
                {
                    logger.LogWarning(
                        "Gemini model {Model} failed with status {StatusCode}. Trying next model if available.",
                        model,
                        (int)response.StatusCode);
                    continue;
                }

                throw lastException;
            }

            using var doc = JsonDocument.Parse(body);
            if (!TryExtractText(doc.RootElement, out var text))
            {
                var reason = BuildMissingTextReason(doc.RootElement);
                throw new InvalidOperationException($"Gemini API response did not contain chat text. {reason}");
            }

            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Gemini API returned an empty response.");

            return new AiChatResponse(text, model, ProviderName);
        }

        throw lastException ?? new InvalidOperationException("No Gemini models available for chat.");
    }

    private IReadOnlyList<string> ResolveCandidateModels()
    {
        var configuredPrimary = configuration["Gemini:Model"] ?? DefaultModel;
        var configuredFallbacks = configuration
            .GetSection("Gemini:FallbackModels")
            .GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToList();

        IReadOnlyList<string> fallbacks = configuredFallbacks.Count > 0
            ? configuredFallbacks
            : DefaultFallbackModels;

        return new[] { configuredPrimary }
            .Concat(fallbacks)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool CanFailoverToNextModel(HttpStatusCode statusCode, string body)
    {
        if (statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout)
        {
            return true;
        }

        var lowerBody = body.ToLowerInvariant();
        return lowerBody.Contains("resource_exhausted", StringComparison.Ordinal)
            || lowerBody.Contains("quota", StringComparison.Ordinal)
            || lowerBody.Contains("rate", StringComparison.Ordinal)
            || lowerBody.Contains("not found", StringComparison.Ordinal)
            || lowerBody.Contains("unavailable", StringComparison.Ordinal);
    }

    private static bool TryExtractText(JsonElement root, out string? text)
    {
        text = null;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
                continue;

            if (!content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in parts.EnumerateArray())
            {
                if (!part.TryGetProperty("text", out var textNode))
                    continue;

                var value = textNode.GetString();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                text = value;
                return true;
            }
        }

        return false;
    }

    private static string BuildMissingTextReason(JsonElement root)
    {
        var details = new List<string>();

        if (root.TryGetProperty("promptFeedback", out var promptFeedback))
        {
            if (promptFeedback.TryGetProperty("blockReason", out var blockReason))
                details.Add($"blockReason={blockReason.GetString()}");

            if (promptFeedback.TryGetProperty("blockReasonMessage", out var blockReasonMessage))
                details.Add($"blockReasonMessage={blockReasonMessage.GetString()}");
        }

        if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (candidate.TryGetProperty("finishReason", out var finishReason))
                {
                    details.Add($"finishReason={finishReason.GetString()}");
                    break;
                }
            }
        }

        if (details.Count == 0)
        {
            var keys = root.ValueKind == JsonValueKind.Object
                ? string.Join(", ", root.EnumerateObject().Select(p => p.Name))
                : "<not-an-object>";
            return $"Top-level keys: {keys}.";
        }

        return string.Join("; ", details);
    }
}
