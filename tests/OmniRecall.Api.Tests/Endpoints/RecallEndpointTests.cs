using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using OmniRecall.Api.Contracts;

namespace OmniRecall.Api.Tests.Endpoints;

public class RecallEndpointTests
{
    [Fact]
    public async Task SearchRecall_AfterUpload_ReturnsCitations()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var form = new MultipartFormDataContent();
        var text = "nebula architecture notes for azure functions and angular app";
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(text)), "file", "nebula-notes.md");
        var uploadResponse = await client.PostAsync("/api/documents/upload", form);
        uploadResponse.EnsureSuccessStatusCode();

        var searchRequest = new RecallSearchRequestDto("nebula", 3);
        var searchResponse = await client.PostAsJsonAsync("/api/recall/search", searchRequest);
        var body = await searchResponse.Content.ReadFromJsonAsync<RecallSearchResponseDto>();

        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Citations);
        Assert.Equal("nebula-notes.md", body.Citations[0].FileName);
    }
}
