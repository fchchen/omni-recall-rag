using System.Text;
using Azure.Storage.Blobs;

namespace OmniRecall.Api.Services;

public sealed class BlobRawDocumentStore(IConfiguration configuration, ILogger<BlobRawDocumentStore> logger) : IRawDocumentStore
{
    private BlobContainerClient? _containerClient;
    private readonly object _lock = new();

    public async Task<string> SaveAsync(
        string fileName,
        string content,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        var client = GetContainerClient();
        await client.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName)
            .Replace(' ', '-')
            .ToLowerInvariant();
        var blobName = $"raw/{DateTime.UtcNow:yyyy/MM/dd}/{contentHash[..12]}-{baseName}{extension}";
        var blobClient = client.GetBlobClient(blobName);

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await blobClient.UploadAsync(stream, overwrite: true, cancellationToken);
        logger.LogInformation("Uploaded raw document to blob path {BlobPath}", blobName);

        return blobName;
    }

    private BlobContainerClient GetContainerClient()
    {
        if (_containerClient is not null)
            return _containerClient;

        lock (_lock)
        {
            if (_containerClient is not null)
                return _containerClient;

            var connectionString = configuration["AzureStorage:BlobConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("AzureStorage:BlobConnectionString is required for Azure storage provider.");

            var containerName = configuration["AzureStorage:BlobContainerName"];
            if (string.IsNullOrWhiteSpace(containerName))
                containerName = "omni-recall-raw";

            _containerClient = new BlobContainerClient(connectionString, containerName);
            return _containerClient;
        }
    }
}
