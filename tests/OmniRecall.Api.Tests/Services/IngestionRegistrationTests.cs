using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniRecall.Api.Services;

namespace OmniRecall.Api.Tests.Services;

public class IngestionRegistrationTests
{
    [Fact]
    public void AddIngestionPersistence_InMemoryProvider_RegistersInMemoryStores()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "InMemory"
            })
            .Build();

        services.AddIngestionPersistence(config);

        Assert.Contains(services, s => s.ServiceType == typeof(IIngestionStore) && s.ImplementationType == typeof(InMemoryIngestionStore));
        Assert.Contains(services, s => s.ServiceType == typeof(IRawDocumentStore) && s.ImplementationType == typeof(InMemoryRawDocumentStore));
        Assert.Contains(services, s => s.ServiceType == typeof(IIngestionStore) && s.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, s => s.ServiceType == typeof(IRawDocumentStore) && s.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddIngestionPersistence_AzureProvider_RegistersAzureStores()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "Azure"
            })
            .Build();

        services.AddIngestionPersistence(config);

        Assert.Contains(services, s => s.ServiceType == typeof(IIngestionStore) && s.ImplementationType == typeof(CosmosIngestionStore));
        Assert.Contains(services, s => s.ServiceType == typeof(IRawDocumentStore) && s.ImplementationType == typeof(BlobRawDocumentStore));
        Assert.Contains(services, s => s.ServiceType == typeof(IIngestionStore) && s.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, s => s.ServiceType == typeof(IRawDocumentStore) && s.Lifetime == ServiceLifetime.Singleton);
    }
}
