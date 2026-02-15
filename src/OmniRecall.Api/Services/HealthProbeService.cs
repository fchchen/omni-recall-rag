using System.Diagnostics;
using System.Net.Http.Headers;
using Azure.Storage.Blobs;
using OmniRecall.Api.Contracts;

namespace OmniRecall.Api.Services;

public interface IHealthProbeService
{
    Task<HealthResponseDto> ProbeAsync(CancellationToken cancellationToken = default);
}

public sealed class HealthProbeService(
    IConfiguration configuration,
    IIngestionStore store,
    IHttpClientFactory httpClientFactory,
    ILogger<HealthProbeService> logger) : IHealthProbeService
{
    private const string Healthy = "healthy";
    private const string Degraded = "degraded";
    private const string Unhealthy = "unhealthy";

    public async Task<HealthResponseDto> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var dependencies = new List<HealthDependencyDto>
        {
            await ProbeIngestionStoreAsync(cancellationToken),
            await ProbeBlobStorageAsync(cancellationToken),
            await ProbeGeminiAsync(cancellationToken),
            await ProbeGitHubModelsAsync(cancellationToken)
        };

        var overall = dependencies.Any(d => string.Equals(d.Status, Unhealthy, StringComparison.OrdinalIgnoreCase))
            ? Unhealthy
            : dependencies.Any(d => string.Equals(d.Status, Degraded, StringComparison.OrdinalIgnoreCase))
                ? Degraded
                : Healthy;

        return new HealthResponseDto(overall, DateTime.UtcNow, dependencies);
    }

    private async Task<HealthDependencyDto> ProbeIngestionStoreAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await store.ListDocumentsAsync(1, cancellationToken);
            return CreateDependency("storage-store", Healthy, "Ingestion store reachable.", sw);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health probe failed for ingestion store.");
            return CreateDependency("storage-store", Unhealthy, $"Ingestion store probe failed: {ex.Message}", sw);
        }
    }

    private async Task<HealthDependencyDto> ProbeBlobStorageAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var provider = configuration["Storage:Provider"]?.Trim();
        if (!provider?.Equals("Azure", StringComparison.OrdinalIgnoreCase) ?? true)
            return CreateDependency("storage-blob", Healthy, "Blob probe skipped (Storage:Provider is not Azure).", sw);

        var connectionString = configuration["AzureStorage:BlobConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return CreateDependency("storage-blob", Degraded, "Azure blob connection string is not configured.", sw);

        var containerName = configuration["AzureStorage:BlobContainerName"];
        if (string.IsNullOrWhiteSpace(containerName))
            containerName = "omni-recall-raw";

        try
        {
            var containerClient = new BlobContainerClient(connectionString, containerName);
            var exists = await containerClient.ExistsAsync(cancellationToken);
            return CreateDependency(
                "storage-blob",
                Healthy,
                exists.Value ? $"Container '{containerName}' is reachable." : $"Container '{containerName}' does not exist yet.",
                sw);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health probe failed for blob storage.");
            return CreateDependency("storage-blob", Unhealthy, $"Blob probe failed: {ex.Message}", sw);
        }
    }

    private async Task<HealthDependencyDto> ProbeGeminiAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return CreateDependency("ai-gemini", Degraded, "Gemini API key is not configured.", sw);

        if (!ShouldProbeExternalAi())
            return CreateDependency("ai-gemini", Healthy, "Gemini is configured (external probe disabled).", sw);

        var baseUrl = (configuration["Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta").TrimEnd('/');
        var url = $"{baseUrl}/models?key={Uri.EscapeDataString(apiKey)}";

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await httpClientFactory.CreateClient().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            var status = (int)response.StatusCode;
            var dependencyStatus = status >= 500 ? Degraded : Healthy;
            return CreateDependency("ai-gemini", dependencyStatus, $"Gemini endpoint reachable (HTTP {status}).", sw);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health probe failed for Gemini endpoint.");
            return CreateDependency("ai-gemini", Unhealthy, $"Gemini probe failed: {ex.Message}", sw);
        }
    }

    private async Task<HealthDependencyDto> ProbeGitHubModelsAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var token = configuration["GitHubModels:Token"];
        if (string.IsNullOrWhiteSpace(token))
            return CreateDependency("ai-github-models", Degraded, "GitHub Models token is not configured.", sw);

        if (!ShouldProbeExternalAi())
            return CreateDependency("ai-github-models", Healthy, "GitHub Models is configured (external probe disabled).", sw);

        var baseUrl = (configuration["GitHubModels:BaseUrl"] ?? "https://models.github.ai/inference").TrimEnd('/');
        var url = $"{baseUrl}/models";

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClientFactory.CreateClient().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            var status = (int)response.StatusCode;
            var dependencyStatus = status >= 500 ? Degraded : Healthy;
            return CreateDependency("ai-github-models", dependencyStatus, $"GitHub Models endpoint reachable (HTTP {status}).", sw);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health probe failed for GitHub Models endpoint.");
            return CreateDependency("ai-github-models", Unhealthy, $"GitHub Models probe failed: {ex.Message}", sw);
        }
    }

    private bool ShouldProbeExternalAi()
    {
        return bool.TryParse(configuration["Health:ProbeExternalAi"], out var enabled) && enabled;
    }

    private static HealthDependencyDto CreateDependency(string name, string status, string detail, Stopwatch sw)
    {
        sw.Stop();
        return new HealthDependencyDto(name, status, detail, sw.ElapsedMilliseconds);
    }
}
