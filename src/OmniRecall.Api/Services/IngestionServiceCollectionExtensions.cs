using Microsoft.Extensions.DependencyInjection.Extensions;

namespace OmniRecall.Api.Services;

public static class IngestionServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Storage:Provider"]?.Trim();
        var useAzure = provider?.Equals("Azure", StringComparison.OrdinalIgnoreCase) == true;

        services.RemoveAll<IIngestionStore>();
        services.RemoveAll<IRawDocumentStore>();

        if (useAzure)
        {
            services.AddSingleton<IIngestionStore, CosmosIngestionStore>();
            services.AddSingleton<IRawDocumentStore, BlobRawDocumentStore>();
            return services;
        }

        services.AddSingleton<IIngestionStore, InMemoryIngestionStore>();
        services.AddSingleton<IRawDocumentStore, InMemoryRawDocumentStore>();
        return services;
    }
}
