using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Services.Storage
{
    public class GoogleCloudStorageProvider : ICloudStorageProvider
    {
        private readonly StorageClient _client;
        private readonly string _bucketName;

        public GoogleCloudStorageProvider(ApiConfiguration.FileStorageConfiguration config)
        {
            var credential = GoogleCredential.FromFile(config.ConnectionString ?? throw new ArgumentNullException(nameof(config.ConnectionString)));
            _client = StorageClient.Create(credential);
            _bucketName = config.BucketName ?? throw new ArgumentNullException(nameof(config.BucketName));
        }

        public async Task<Stream> GetAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            var objectName = BuildObjectName(applicationToken, fileId);
            var stream = new MemoryStream();
            await _client.DownloadObjectAsync(_bucketName, objectName, stream, cancellationToken: ct);
            stream.Position = 0;
            return stream;
        }

        public async Task<string> PutAsync(string applicationToken, string fileId, Stream content, long contentLength, CancellationToken ct = default)
        {
            var objectName = BuildObjectName(applicationToken, fileId);
            await _client.UploadObjectAsync(_bucketName, objectName, contentType: null, content, cancellationToken: ct);
            return $"gs://{_bucketName}/{objectName}";
        }

        public async Task DeleteAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            var objectName = BuildObjectName(applicationToken, fileId);
            await _client.DeleteObjectAsync(_bucketName, objectName, cancellationToken: ct);
        }

        public async Task<bool> ExistsAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            try
            {
                var objectName = BuildObjectName(applicationToken, fileId);
                await _client.GetObjectAsync(_bucketName, objectName, cancellationToken: ct);
                return true;
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public async Task<long> GetSizeAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            var objectName = BuildObjectName(applicationToken, fileId);
            var obj = await _client.GetObjectAsync(_bucketName, objectName, cancellationToken: ct);
            return (long)(obj.Size ?? 0UL);
        }

        private static string BuildObjectName(string applicationToken, string fileId)
            => $"{applicationToken}/files/{fileId}";
    }
}
