using OmniRecall.Api.Endpoints;
using OmniRecall.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AiRoutingOptions>(builder.Configuration.GetSection("AiRouting"));
builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection("Ingestion"));
builder.Services.Configure<ChatQualityOptions>(builder.Configuration.GetSection("ChatQuality"));
builder.Services.AddHttpClient<GeminiChatClient>();
builder.Services.AddHttpClient<GitHubModelsChatClient>();
builder.Services.AddHttpClient<GeminiEmbeddingClient>();
builder.Services.AddHttpClient<AzureDocumentIntelligenceOcrTextExtractor>();
builder.Services.AddSingleton<ITextChunker, SlidingWindowTextChunker>();
builder.Services.AddScoped<IOcrTextExtractor>(sp =>
{
    var provider = builder.Configuration["Ocr:Provider"]?.Trim();
    if (provider?.Equals("AzureDocumentIntelligence", StringComparison.OrdinalIgnoreCase) == true)
        return sp.GetRequiredService<AzureDocumentIntelligenceOcrTextExtractor>();

    return new NoOpOcrTextExtractor();
});
builder.Services.AddScoped<IPdfTextExtractor, PdfPigTextExtractor>();
builder.Services.AddIngestionPersistence(builder.Configuration);
builder.Services.AddScoped<IEmbeddingClient>(sp =>
{
    var provider = builder.Configuration["Embeddings:Provider"]?.Trim();
    if (provider?.Equals("Gemini", StringComparison.OrdinalIgnoreCase) == true)
        return sp.GetRequiredService<GeminiEmbeddingClient>();

    return new NoOpEmbeddingClient();
});
builder.Services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
builder.Services.AddScoped<IRecallSearchService, RecallSearchService>();
builder.Services.AddScoped<IChatOrchestrationService, ChatOrchestrationService>();
builder.Services.AddScoped<AiChatRouter>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiRoutingOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<AiChatRouter>>();
    var primary = sp.GetRequiredService<GeminiChatClient>();
    var fallback = sp.GetRequiredService<GitHubModelsChatClient>();
    return new AiChatRouter(primary, fallback, options, logger);
});

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
        if (exception is not null)
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("GlobalExceptionHandler");
            logger.LogError(exception, "Unhandled exception for request {Path}", context.Request.Path);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Title = "Unexpected server error",
            Detail = "An unexpected error occurred while processing the request.",
            Status = StatusCodes.Status500InternalServerError
        };
        await context.Response.WriteAsJsonAsync(problem);
    });
});

app.MapChatEndpoints();
app.MapDocumentEndpoints();
app.MapRecallEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithTags("Health");

app.Run();

public partial class Program;
