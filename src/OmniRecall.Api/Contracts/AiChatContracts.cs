namespace OmniRecall.Api.Contracts;

public sealed record AiChatRequest(string Prompt);

public sealed record AiChatResponse(string Text, string Model, string Provider);
