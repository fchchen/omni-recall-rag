using OmniRecall.Api.Contracts;

namespace OmniRecall.Api.Services;

public interface IAiChatClient
{
    string ProviderName { get; }
    Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default);
}
