using OmniRecall.Api.Contracts;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Endpoints;

public static class DocumentEndpoints
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".txt",
        ".md",
        ".markdown"
    };

    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents")
            .WithTags("Documents");

        group.MapPost("/upload", UploadDocument)
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<UploadDocumentResponseDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapGet("/{documentId}", GetDocument)
            .Produces<DocumentDetailsDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/", ListDocuments)
            .Produces<IReadOnlyList<DocumentListItemDto>>();
        group.MapGet("/{documentId}/chunks", GetDocumentChunks)
            .Produces<IReadOnlyList<DocumentChunkPreviewDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/{documentId}", DeleteDocument)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{documentId}/reindex", ReindexDocument)
            .Produces<ReindexDocumentResponseDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> UploadDocument(
        HttpRequest request,
        IDocumentIngestionService ingestionService,
        Microsoft.Extensions.Options.IOptions<IngestionOptions> ingestionOptions,
        IPdfTextExtractor pdfTextExtractor,
        CancellationToken cancellationToken)
    {
        var maxUploadBytes = Math.Max(1, ingestionOptions.Value.MaxUploadBytes);
        if (request.ContentLength is > 0 and var contentLength && contentLength > maxUploadBytes)
        {
            return Results.Problem(
                title: "Payload too large",
                detail: $"Max upload size is {maxUploadBytes} bytes.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (!request.HasFormContentType)
            return Results.BadRequest(new { error = "Expected multipart form data." });

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new { error = "Invalid multipart form payload." });
        }

        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "File is required." });
        if (file.Length > maxUploadBytes)
        {
            return Results.Problem(
                title: "Payload too large",
                detail: $"Max upload size is {maxUploadBytes} bytes.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

        await using var stream = file.OpenReadStream();
        var content = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? await pdfTextExtractor.ExtractTextAsync(stream, cancellationToken)
            : await ReadTextContentAsync(stream, cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
            return Results.BadRequest(new { error = "Uploaded file produced no readable text content." });

        var sourceType = form.TryGetValue("sourceType", out var sourceValues) && !string.IsNullOrWhiteSpace(sourceValues)
            ? sourceValues.ToString()
            : "file";

        var result = await ingestionService.IngestAsync(file.FileName, content, sourceType, cancellationToken);
        var response = new UploadDocumentResponseDto(
            result.DocumentId,
            result.FileName,
            result.SourceType,
            result.BlobPath,
            result.ChunkCount,
            result.ContentHash,
            result.CreatedAtUtc);

        return Results.Created($"/api/documents/{result.DocumentId}", response);
    }

    private static async Task<IResult> GetDocument(
        string documentId,
        IDocumentIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var document = await ingestionService.GetDocumentAsync(documentId, cancellationToken);
        if (document is null)
            return Results.NotFound(new { error = "Document not found." });

        return Results.Ok(new DocumentDetailsDto(
            document.DocumentId,
            document.FileName,
            document.SourceType,
            document.BlobPath,
            document.ChunkCount,
            document.ContentHash,
            document.CreatedAtUtc));
    }

    private static async Task<IResult> ListDocuments(
        [AsParameters] ListDocumentsQuery query,
        IDocumentIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var max = query.MaxCount is > 0 ? query.MaxCount.Value : 100;
        var docs = await ingestionService.ListDocumentsAsync(max, cancellationToken);
        var result = docs.Select(d => new DocumentListItemDto(
            d.DocumentId,
            d.FileName,
            d.SourceType,
            d.ChunkCount,
            d.CreatedAtUtc));
        return Results.Ok(result);
    }

    private static async Task<IResult> GetDocumentChunks(
        string documentId,
        [AsParameters] ListChunksQuery query,
        IDocumentIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var document = await ingestionService.GetDocumentAsync(documentId, cancellationToken);
        if (document is null)
            return Results.NotFound(new { error = "Document not found." });

        var max = query.MaxCount is > 0 ? query.MaxCount.Value : 200;
        var chunks = await ingestionService.GetDocumentChunksAsync(documentId, max, cancellationToken);
        var result = chunks.Select(c => new DocumentChunkPreviewDto(
            c.ChunkId,
            c.ChunkIndex,
            c.Snippet,
            c.HasEmbedding,
            c.CreatedAtUtc));
        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteDocument(
        string documentId,
        IDocumentIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var deleted = await ingestionService.DeleteDocumentAsync(documentId, cancellationToken);
        if (!deleted)
            return Results.NotFound(new { error = "Document not found." });

        return Results.NoContent();
    }

    private static async Task<IResult> ReindexDocument(
        string documentId,
        IDocumentIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var result = await ingestionService.ReindexDocumentAsync(documentId, cancellationToken);
        if (result is null)
            return Results.NotFound(new { error = "Document not found." });

        return Results.Ok(new ReindexDocumentResponseDto(
            result.DocumentId,
            result.ChunkCount,
            result.EmbeddedCount,
            result.RateLimitedCount,
            result.EmptyCount,
            result.FailedCount,
            result.ReindexedAtUtc));
    }

    private static async Task<string> ReadTextContentAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public sealed class ListDocumentsQuery
    {
        public int? MaxCount { get; init; }
    }

    public sealed class ListChunksQuery
    {
        public int? MaxCount { get; init; }
    }
}
