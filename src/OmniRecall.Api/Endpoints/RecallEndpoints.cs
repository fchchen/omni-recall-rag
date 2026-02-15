using OmniRecall.Api.Contracts;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Endpoints;

public static class RecallEndpoints
{
    public static IEndpointRouteBuilder MapRecallEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recall")
            .WithTags("Recall");

        group.MapPost("/search", SearchRecall)
            .Produces<RecallSearchResponseDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> SearchRecall(
        RecallSearchRequestDto request,
        IRecallSearchService recallSearchService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return Results.BadRequest(new { error = "Query is required." });

        var result = await recallSearchService.SearchAsync(request.Query, request.TopK, cancellationToken);
        return Results.Ok(result);
    }
}
