using OmniRecall.Api.Contracts;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat")
            .WithTags("Chat");

        group.MapPost("/", CompleteChat)
            .Produces<ChatResponseDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> CompleteChat(
        ChatRequestDto request,
        IChatOrchestrationService orchestrationService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return Results.BadRequest(new { error = "Prompt is required." });

        try
        {
            var response = await orchestrationService.CompleteAsync(request.Prompt, request.TopK, cancellationToken);
            return Results.Ok(response);
        }
        catch (AiProviderUnavailableException ex)
        {
            return Results.Problem(
                title: "AI provider unavailable",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
