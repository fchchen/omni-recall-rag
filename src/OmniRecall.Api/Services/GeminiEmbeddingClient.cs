using System.Net;
using System.Text;
using System.Text.Json;

namespace OmniRecall.Api.Services;

public sealed class GeminiEmbeddingClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GeminiEmbeddingClient> logger) : IEmbeddingClient
{
    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private static readonly string[] DefaultModelCandidates = ["gemini-embedding-001", "embedding-001"];

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new EmbeddingResult([], EmbeddingStatus.Empty, Message: "Input text is empty.");

        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return new EmbeddingResult([], EmbeddingStatus.Empty, Message: "Gemini API key missing.");

        var baseUrl = configuration["Gemini:BaseUrl"] ?? DefaultBaseUrl;
        var modelCandidates = BuildModelCandidates(configuration["Gemini:EmbeddingModel"]);

        foreach (var model in modelCandidates)
        {
            var url = $"{baseUrl}/models/{model}:embedContent?key={Uri.EscapeDataString(apiKey)}";
            var payload = JsonSerializer.Serialize(new
            {
                model = $"models/{model}",
                content = new
                {
                    parts = new[]
                    {
                        new { text }
                    }
                }
            });

            try
            {
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await httpClient.PostAsync(url, content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    logger.LogWarning("Gemini embeddings rate-limited for model {Model}", model);
                    return new EmbeddingResult([], EmbeddingStatus.RateLimited, model);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    logger.LogWarning("Gemini embedding model {Model} not available for embedContent. Trying next.", model);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    logger.LogWarning("Gemini embeddings auth rejected ({StatusCode})", response.StatusCode);
                    return new EmbeddingResult([], EmbeddingStatus.Error, model, $"Auth rejected: {response.StatusCode}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Gemini embeddings API returned {StatusCode} for model {Model}. Falling back to empty embeddings.",
                        response.StatusCode,
                        model);
                    return new EmbeddingResult([], EmbeddingStatus.Error, model, $"HTTP {(int)response.StatusCode}");
                }

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("embedding", out var embeddingElement))
                    return new EmbeddingResult([], EmbeddingStatus.Empty, model, "Missing embedding property.");
                if (!embeddingElement.TryGetProperty("values", out var valuesElement))
                    return new EmbeddingResult([], EmbeddingStatus.Empty, model, "Missing embedding values.");

                var values = new List<float>();
                foreach (var value in valuesElement.EnumerateArray())
                {
                    if (value.TryGetSingle(out var floatValue))
                        values.Add(floatValue);
                }

                var status = values.Count > 0 ? EmbeddingStatus.Success : EmbeddingStatus.Empty;
                return new EmbeddingResult(values, status, model);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Gemini embeddings request failed for model {Model}. Trying next.", model);
            }
        }

        logger.LogWarning("No compatible Gemini embedding model found. Returning empty embeddings.");
        return new EmbeddingResult([], EmbeddingStatus.NotSupported, Message: "No compatible Gemini embedding model.");
    }

    private static IReadOnlyList<string> BuildModelCandidates(string? configuredModel)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredModel))
        {
            candidates.Add(NormalizeModel(configuredModel));
        }

        foreach (var defaultModel in DefaultModelCandidates)
        {
            if (!candidates.Contains(defaultModel, StringComparer.OrdinalIgnoreCase))
                candidates.Add(defaultModel);
        }

        return candidates;
    }

    private static string NormalizeModel(string model)
    {
        var trimmed = model.Trim();
        return trimmed.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? trimmed["models/".Length..]
            : trimmed;
    }
}
