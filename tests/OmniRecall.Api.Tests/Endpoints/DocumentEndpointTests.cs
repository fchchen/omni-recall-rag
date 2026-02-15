using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniRecall.Api.Contracts;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Endpoints;

public class DocumentEndpointTests
{
    [Fact]
    public async Task UploadDocument_NoFile_ReturnsBadRequest()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/documents/upload", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadDocument_TextFile_ReturnsDocumentIdAndChunks()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var content = new MultipartFormDataContent();
        var text = string.Join(' ', Enumerable.Range(1, 250).Select(i => $"memory{i}"));
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(text));
        content.Add(fileContent, "file", "memory-notes.txt");

        var uploadResponse = await client.PostAsync("/api/documents/upload", content);
        var body = await uploadResponse.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();

        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.DocumentId));
        Assert.True(body.ChunkCount >= 2);
    }

    [Fact]
    public async Task UploadDocument_FileTooLarge_ReturnsPayloadTooLarge()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Ingestion:MaxUploadBytes"] = "12"
                    });
                });
            });
        var client = factory.CreateClient();

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("this-content-is-too-large"));
        content.Add(fileContent, "file", "oversized.txt");

        var response = await client.PostAsync("/api/documents/upload", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task GetDocument_AfterUpload_ReturnsPersistedMetadata()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var content = new MultipartFormDataContent();
        var text = string.Join(' ', Enumerable.Range(1, 30).Select(i => $"decision{i}"));
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(text));
        content.Add(fileContent, "file", "decisions.md");

        var uploadResponse = await client.PostAsync("/api/documents/upload", content);
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();
        Assert.NotNull(uploadBody);

        var getResponse = await client.GetAsync($"/api/documents/{uploadBody!.DocumentId}");
        var getBody = await getResponse.Content.ReadFromJsonAsync<DocumentDetailsDto>();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(getBody);
        Assert.Equal(uploadBody.DocumentId, getBody!.DocumentId);
        Assert.Equal(uploadBody.ChunkCount, getBody.ChunkCount);
    }

    [Fact]
    public async Task UploadDocument_PdfFile_ReturnsDocumentIdAndChunks()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IPdfTextExtractor>();
                    services.AddSingleton<IPdfTextExtractor>(new StubPdfTextExtractor(
                        "pdf extracted text about azure functions architecture decisions"));
                });
            });

        var client = factory.CreateClient();

        var content = new MultipartFormDataContent();
        var fakePdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var fileContent = new ByteArrayContent(fakePdfBytes);
        content.Add(fileContent, "file", "decision-log.pdf");

        var uploadResponse = await client.PostAsync("/api/documents/upload", content);
        var body = await uploadResponse.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();

        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.DocumentId));
        Assert.True(body.ChunkCount >= 1);
    }

    [Fact]
    public async Task ListDocuments_AfterUpload_ReturnsItem()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var content = new MultipartFormDataContent();
        var text = "list documents integration sample";
        content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(text)), "file", "list-check.md");
        var uploadResponse = await client.PostAsync("/api/documents/upload", content);
        uploadResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/documents");
        var docs = await response.Content.ReadFromJsonAsync<List<DocumentListItemDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(docs);
        Assert.Contains(docs!, d => d.FileName == "list-check.md");
    }

    [Fact]
    public async Task GetDocumentChunks_AfterUpload_ReturnsChunkPreviews()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var content = new MultipartFormDataContent();
        var text = string.Join(' ', Enumerable.Range(1, 240).Select(i => $"chunkword{i}"));
        content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(text)), "file", "chunk-check.md");
        var uploadResponse = await client.PostAsync("/api/documents/upload", content);
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();
        Assert.NotNull(uploadBody);

        var response = await client.GetAsync($"/api/documents/{uploadBody!.DocumentId}/chunks");
        var chunks = await response.Content.ReadFromJsonAsync<List<DocumentChunkPreviewDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(chunks);
        Assert.NotEmpty(chunks!);
        Assert.All(chunks!, c => Assert.False(string.IsNullOrWhiteSpace(c.Snippet)));
    }

    [Fact]
    public async Task DeleteDocument_RemovesDocument()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("delete me")), "file", "delete-check.md");
        var uploadResponse = await client.PostAsync("/api/documents/upload", content);
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();
        Assert.NotNull(uploadBody);

        var deleteResponse = await client.DeleteAsync($"/api/documents/{uploadBody!.DocumentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/documents/{uploadBody.DocumentId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task ReindexDocument_AfterUpload_ReturnsChunkCount()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IEmbeddingClient>();
                    services.AddSingleton<IEmbeddingClient>(new StubEmbeddingClient([0.01f, 0.02f, 0.03f]));
                });
            });

        var client = factory.CreateClient();

        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("reindex operation for embeddings")), "file", "reindex-check.md");
        var uploadResponse = await client.PostAsync("/api/documents/upload", content);
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();
        Assert.NotNull(uploadBody);

        var reindexResponse = await client.PostAsync($"/api/documents/{uploadBody!.DocumentId}/reindex", null);
        var reindexBody = await reindexResponse.Content.ReadFromJsonAsync<ReindexDocumentResponseDto>();

        Assert.Equal(HttpStatusCode.OK, reindexResponse.StatusCode);
        Assert.NotNull(reindexBody);
        Assert.Equal(uploadBody.DocumentId, reindexBody!.DocumentId);
        Assert.True(reindexBody.ChunkCount >= 1);
        Assert.True(reindexBody.EmbeddedCount >= 1);
        Assert.Equal(0, reindexBody.FailedCount);
    }
}

internal sealed class StubPdfTextExtractor(string text) : IPdfTextExtractor
{
    public Task<string> ExtractTextAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(text);
    }
}

internal sealed class StubEmbeddingClient(IReadOnlyList<float> vector) : IEmbeddingClient
{
    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EmbeddingResult(vector, EmbeddingStatus.Success, "stub"));
    }
}
