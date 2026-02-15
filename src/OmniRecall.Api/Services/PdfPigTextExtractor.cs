using System.Text;
using UglyToad.PdfPig;

namespace OmniRecall.Api.Services;

public sealed class PdfPigTextExtractor(
    IOcrTextExtractor ocrTextExtractor,
    IConfiguration configuration,
    ILogger<PdfPigTextExtractor> logger) : IPdfTextExtractor
{
    public async Task<string> ExtractTextAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        if (pdfStream is null)
            throw new ArgumentNullException(nameof(pdfStream));

        var minChars = configuration.GetValue("Ocr:PdfTextMinChars", 120);
        await using var memory = new MemoryStream();
        await pdfStream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();

        var extracted = string.Empty;
        try
        {
            await using var pdfMemory = new MemoryStream(bytes);
            extracted = ExtractWithPdfPig(pdfMemory, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Primary PDF text extraction failed; attempting OCR fallback.");
        }

        if (!string.IsNullOrWhiteSpace(extracted) && extracted.Length >= minChars)
            return extracted;

        await using var ocrMemory = new MemoryStream(bytes);
        var ocrText = await ocrTextExtractor.ExtractTextAsync(ocrMemory, cancellationToken);
        if (!string.IsNullOrWhiteSpace(ocrText))
            return ocrText.Trim();

        return extracted.Trim();
    }

    private static string ExtractWithPdfPig(Stream pdfStream, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        using (var document = PdfDocument.Open(pdfStream))
        {
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine(page.Text);
            }
        }

        return sb.ToString().Trim();
    }
}
