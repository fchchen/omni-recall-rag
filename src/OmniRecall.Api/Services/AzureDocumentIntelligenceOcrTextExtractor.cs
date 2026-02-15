using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OmniRecall.Api.Services;

public sealed class AzureDocumentIntelligenceOcrTextExtractor(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<AzureDocumentIntelligenceOcrTextExtractor> logger) : IOcrTextExtractor
{
    public async Task<string> ExtractTextAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        var endpoint = configuration["Ocr:Endpoint"]?.TrimEnd('/');
        var key = configuration["Ocr:Key"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var apiVersion = configuration["Ocr:ApiVersion"];
        if (string.IsNullOrWhiteSpace(apiVersion))
            apiVersion = "2024-11-30";

        var pollMs = configuration.GetValue("Ocr:PollMs", 800);
        var maxPollAttempts = configuration.GetValue("Ocr:MaxPollAttempts", 20);

        try
        {
            await using var mem = new MemoryStream();
            await fileStream.CopyToAsync(mem, cancellationToken);
            mem.Position = 0;

            var analyzeUri = $"{endpoint}/documentintelligence/documentModels/prebuilt-read:analyze?api-version={apiVersion}";
            using var analyzeRequest = new HttpRequestMessage(HttpMethod.Post, analyzeUri);
            analyzeRequest.Headers.Add("Ocp-Apim-Subscription-Key", key);
            analyzeRequest.Content = new ByteArrayContent(mem.ToArray());
            analyzeRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

            using var analyzeResponse = await httpClient.SendAsync(analyzeRequest, cancellationToken);
            if (analyzeResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("OCR analyze request rejected with {StatusCode}", analyzeResponse.StatusCode);
                return string.Empty;
            }

            if (!analyzeResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("OCR analyze request failed with {StatusCode}", analyzeResponse.StatusCode);
                return string.Empty;
            }

            if (!analyzeResponse.Headers.TryGetValues("operation-location", out var values))
                return string.Empty;

            var operationLocation = values.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(operationLocation))
                return string.Empty;

            for (var attempt = 1; attempt <= maxPollAttempts; attempt++)
            {
                await Task.Delay(pollMs, cancellationToken);
                using var statusRequest = new HttpRequestMessage(HttpMethod.Get, operationLocation);
                statusRequest.Headers.Add("Ocp-Apim-Subscription-Key", key);

                using var statusResponse = await httpClient.SendAsync(statusRequest, cancellationToken);
                var statusBody = await statusResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!statusResponse.IsSuccessStatusCode)
                    continue;

                using var doc = JsonDocument.Parse(statusBody);
                var status = doc.RootElement.TryGetProperty("status", out var statusElement)
                    ? statusElement.GetString()?.ToLowerInvariant()
                    : null;

                if (status is "running" or "notstarted")
                    continue;

                if (status == "succeeded")
                {
                    if (doc.RootElement.TryGetProperty("analyzeResult", out var analyzeResult))
                    {
                        if (analyzeResult.TryGetProperty("content", out var contentElement))
                            return contentElement.GetString()?.Trim() ?? string.Empty;
                    }

                    if (doc.RootElement.TryGetProperty("content", out var rootContent))
                        return rootContent.GetString()?.Trim() ?? string.Empty;

                    return string.Empty;
                }

                if (status == "failed")
                    return string.Empty;
            }

            logger.LogWarning("OCR polling timed out after {Attempts} attempts", maxPollAttempts);
            return string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "OCR extraction failed.");
            return string.Empty;
        }
    }
}
