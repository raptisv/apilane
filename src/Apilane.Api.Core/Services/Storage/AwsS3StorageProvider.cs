using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Services.Storage
{
    public class AwsS3StorageProvider : ICloudStorageProvider
    {
        private readonly AmazonS3Client _client;
        private readonly string _bucketName;

        public AwsS3StorageProvider(ApiConfiguration.FileStorageConfiguration config)
        {
            var parts = ParseConnectionString(config.ConnectionString ?? throw new ArgumentNullException(nameof(config.ConnectionString)));
            var s3Config = new AmazonS3Config();

            if (!string.IsNullOrWhiteSpace(parts.Endpoint))
            {
                s3Config.ServiceURL = $"https://{parts.Endpoint}";
                s3Config.ForcePathStyle = true;
            }
            else if (!string.IsNullOrWhiteSpace(parts.Region))
            {
                s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(parts.Region);
            }

            _client = new AmazonS3Client(parts.AccessKeyId, parts.SecretAccessKey, s3Config);
            _bucketName = config.BucketName ?? throw new ArgumentNullException(nameof(config.BucketName));
        }

        public async Task<Stream> GetAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            var key = BuildKey(applicationToken, fileId);
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            var response = await _client.GetObjectAsync(request, ct);
            return response.ResponseStream;
        }

        public async Task<string> PutAsync(string applicationToken, string fileId, Stream content, long contentLength, CancellationToken ct = default)
        {
            var key = BuildKey(applicationToken, fileId);
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = content,
                AutoCloseStream = false
            };

            await _client.PutObjectAsync(request, ct);
            return $"s3://{_bucketName}/{key}";
        }

        public async Task DeleteAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            var key = BuildKey(applicationToken, fileId);
            await _client.DeleteObjectAsync(_bucketName, key, ct);
        }

        public async Task<bool> ExistsAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            try
            {
                var key = BuildKey(applicationToken, fileId);
                await _client.GetObjectMetadataAsync(_bucketName, key, ct);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public async Task<long> GetSizeAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            var key = BuildKey(applicationToken, fileId);
            var metadata = await _client.GetObjectMetadataAsync(_bucketName, key, ct);
            return metadata.ContentLength;
        }

        private static string BuildKey(string applicationToken, string fileId)
            => $"{applicationToken}/files/{fileId}";

        private static (string? Endpoint, string? Region, string AccessKeyId, string SecretAccessKey) ParseConnectionString(string connectionString)
        {
            var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var keyValue = part.Split('=', 2);
                if (keyValue.Length == 2)
                {
                    parts[keyValue[0].Trim()] = keyValue[1].Trim();
                }
            }

            return (
                parts.GetValueOrDefault("endpoint"),
                parts.GetValueOrDefault("region"),
                parts.GetValueOrDefault("accessKeyId") ?? throw new ArgumentException("Missing 'accessKeyId' in connection string", nameof(connectionString)),
                parts.GetValueOrDefault("secretAccessKey") ?? throw new ArgumentException("Missing 'secretAccessKey' in connection string", nameof(connectionString)));
        }
    }
}
