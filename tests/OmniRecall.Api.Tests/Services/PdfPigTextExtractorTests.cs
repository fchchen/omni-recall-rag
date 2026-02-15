using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Services;

public class PdfPigTextExtractorTests
{
    [Fact]
    public async Task ExtractTextAsync_WhenPdfParsingFails_UsesOcrFallback()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ocr:PdfTextMinChars"] = "120"
            })
            .Build();
        var ocr = new StubOcrTextExtractor("text from ocr fallback");
        var sut = new PdfPigTextExtractor(ocr, config, NullLogger<PdfPigTextExtractor>.Instance);

        await using var stream = new MemoryStream([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37]);
        var text = await sut.ExtractTextAsync(stream);

        Assert.Equal("text from ocr fallback", text);
    }

    [Fact]
    public async Task ExtractTextAsync_WhenNoOcrText_ReturnsEmpty()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ocr:PdfTextMinChars"] = "120"
            })
            .Build();
        var ocr = new StubOcrTextExtractor("");
        var sut = new PdfPigTextExtractor(ocr, config, NullLogger<PdfPigTextExtractor>.Instance);

        await using var stream = new MemoryStream([0x25, 0x50, 0x44, 0x46]);
        var text = await sut.ExtractTextAsync(stream);

        Assert.True(string.IsNullOrWhiteSpace(text));
    }
}

internal sealed class StubOcrTextExtractor(string value) : IOcrTextExtractor
{
    public Task<string> ExtractTextAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(value);
    }
}
