using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OmniRecall.Api.Contracts;

namespace OmniRecall.Api.Services;

public sealed class GitHubModelsChatClient(HttpClient httpClient, IConfiguration configuration) : IAiChatClient
{
    private const string DefaultBaseUrl = "https://models.github.ai/inference";
    private const string DefaultModel = "deepseek/DeepSeek-V3-0324";

    public string ProviderName => "github-models";

    public async Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        var token = configuration["GitHubModels:Token"];
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("GitHub Models token not configured.");

        var baseUrl = configuration["GitHubModels:BaseUrl"] ?? DefaultBaseUrl;
        var model = configuration["GitHubModels:Model"] ?? DefaultModel;
        var url = $"{baseUrl.TrimEnd('/')}/chat/completions";

        var payload = JsonSerializer.Serialize(new
        {
            model,
            messages = new[]
            {
                new { role = "user", content = request.Prompt }
            },
            temperature = 0.2
        });

        using var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new AiRateLimitException("GitHub Models API rate limited.");

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub Models API returned {response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        if (!TryExtractContent(doc.RootElement, out var text))
        {
            var reason = BuildMissingContentReason(doc.RootElement);
            throw new InvalidOperationException($"GitHub Models API response did not contain chat text. {reason}");
        }

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("GitHub Models API returned an empty response.");

        return new AiChatResponse(text, model, ProviderName);
    }

    private static bool TryExtractContent(JsonElement root, out string? text)
    {
        text = null;
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
                continue;

            if (!message.TryGetProperty("content", out var content))
                continue;

            switch (content.ValueKind)
            {
                case JsonValueKind.String:
                {
                    var value = content.GetString();
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    text = value;
                    return true;
                }
                case JsonValueKind.Array:
                {
                    var parts = new List<string>();
                    foreach (var item in content.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var raw = item.GetString();
                            if (!string.IsNullOrWhiteSpace(raw))
                                parts.Add(raw);
                            continue;
                        }

                        if (item.ValueKind != JsonValueKind.Object)
                            continue;

                        if (item.TryGetProperty("text", out var textNode))
                        {
                            var piece = textNode.GetString();
                            if (!string.IsNullOrWhiteSpace(piece))
                                parts.Add(piece);
                        }
                    }

                    if (parts.Count == 0)
                        continue;

                    text = string.Join(string.Empty, parts);
                    return true;
                }
            }
        }

        return false;
    }

    private static string BuildMissingContentReason(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return "Response root was not a JSON object.";

        var keys = string.Join(", ", root.EnumerateObject().Select(p => p.Name));
        return $"Top-level keys: {keys}.";
    }
}
