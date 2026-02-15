using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniRecall.Api.Contracts;
using OmniRecall.Api.Data.Models;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Endpoints;

public class HealthEndpointTests
{
    [Fact]
    public async Task GetHealth_ReturnsDependencyReport()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<HealthResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.True(body!.Dependencies.Count >= 3);
        Assert.Contains(body.Dependencies, d => d.Name == "storage-store");
        Assert.Contains(body.Dependencies, d => d.Name == "ai-gemini");
        Assert.Contains(body.Dependencies, d => d.Name == "ai-github-models");
    }

    [Fact]
    public async Task GetHealth_WhenStoreProbeFails_ReturnsServiceUnavailable()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IIngestionStore>();
                    services.AddSingleton<IIngestionStore, ThrowingIngestionStore>();
                });
            });

        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<HealthResponseDto>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("unhealthy", body!.Status);
        Assert.Contains(body.Dependencies, d => d.Name == "storage-store" && d.Status == "unhealthy");
    }

    [Fact]
    public async Task GetSwaggerJson_ReturnsOpenApiDocument()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"openapi\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"paths\"", json, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class ThrowingIngestionStore : IIngestionStore
{
    public Task<CosmosDocumentRecord> UpsertDocumentAsync(CosmosDocumentRecord document, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task UpsertChunksAsync(IReadOnlyList<CosmosChunkRecord> chunks, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<CosmosDocumentRecord?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<IReadOnlyList<CosmosDocumentRecord>> ListDocumentsAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("store unavailable");
    }

    public Task<IReadOnlyList<CosmosChunkRecord>> GetChunksByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<IReadOnlyList<CosmosChunkRecord>> GetRecentChunksAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<IReadOnlyDictionary<string, CosmosDocumentRecord>> GetDocumentsByIdsAsync(
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}
