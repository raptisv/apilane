using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Azure.Storage.Blobs;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Services.Storage
{
    public class AzureBlobStorageProvider : ICloudStorageProvider
    {
        private readonly BlobContainerClient _containerClient;

        public AzureBlobStorageProvider(ApiConfiguration.FileStorageConfiguration config)
        {
            var blobServiceClient = new BlobServiceClient(config.ConnectionString ?? throw new ArgumentNullException(nameof(config.ConnectionString)));
            _containerClient = blobServiceClient.GetBlobContainerClient(config.BucketName ?? throw new ArgumentNullException(nameof(config.BucketName)));
        }

        public async Task<Stream> GetAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            var blobClient = _containerClient.GetBlobClient(BuildBlobName(applicationToken, fileId));
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
            return response.Value.Content;
        }

        public async Task<string> PutAsync(string applicationToken, string fileId, Stream content, long contentLength, CancellationToken ct = default)
        {
            var blobClient = _containerClient.GetBlobClient(BuildBlobName(applicationToken, fileId));
            await blobClient.UploadAsync(content, overwrite: true, cancellationToken: ct);
            return blobClient.Uri.ToString();
        }

        public async Task DeleteAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            var blobClient = _containerClient.GetBlobClient(BuildBlobName(applicationToken, fileId));
            await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
        }

        public async Task<bool> ExistsAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            var blobClient = _containerClient.GetBlobClient(BuildBlobName(applicationToken, fileId));
            var response = await blobClient.ExistsAsync(cancellationToken: ct);
            return response.Value;
        }

        public async Task<long> GetSizeAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            var blobClient = _containerClient.GetBlobClient(BuildBlobName(applicationToken, fileId));
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: ct);
            return properties.Value.ContentLength;
        }

        private static string BuildBlobName(string applicationToken, string fileId)
            => $"{applicationToken}/files/{fileId}";
    }
}
