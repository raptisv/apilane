using Apilane.Common.Enums;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace Apilane.Api.Core.Configuration
{
    public enum StorageProviderType
    {
        LocalFileSystem = 0,
        GoogleCloudStorage = 1,
        AwsS3 = 2,
        AzureBlobStorage = 3
    }

    public class ApiConfiguration
    {
        public HostingEnvironment Environment { get; set; }
        public string Url { get; set; }
        public string FilesPath { get; set; }
        public FileStorageConfiguration FileStorage { get; set; }
        public string PortalUrl { get; set; }
        public string InstallationKey { get; set; }
        public int? MinThreads { get; set; }
        public List<string> InvalidFilesExtentions { get; set; }
        public OpenTelemetryConfiguration OpenTelemetry { get; set; }
        public ClusteringConfiguration Clustering { get; set; }

        public ApiConfiguration(IConfiguration configuration)
        {
            Environment = configuration.GetValue<HostingEnvironment>("Environment");
            Url = configuration.GetValue<string>("Url") ?? throw new ArgumentNullException(nameof(Url));
            FilesPath = configuration.GetValue<string>("FilesPath") ?? throw new ArgumentNullException(nameof(FilesPath));
            FileStorage = configuration.GetSection("FileStorage").Get<FileStorageConfiguration>() ?? new FileStorageConfiguration();
            PortalUrl = configuration.GetValue<string>("PortalUrl") ?? throw new ArgumentNullException(nameof(PortalUrl));
            InstallationKey = configuration.GetValue<string>("InstallationKey") ?? throw new ArgumentNullException(nameof(InstallationKey));
            MinThreads = configuration.GetValue<int?>("MinThreads") ?? throw new ArgumentNullException(nameof(MinThreads));
            InvalidFilesExtentions = configuration.GetSection("InvalidFilesExtentions").Get<List<string>>() ?? throw new ArgumentNullException(nameof(InvalidFilesExtentions));
            OpenTelemetry = configuration.GetSection("OpenTelemetry").Get<OpenTelemetryConfiguration>() ?? throw new ArgumentNullException(nameof(OpenTelemetry));
            Clustering = configuration.GetSection("Clustering").Get<ClusteringConfiguration>() ?? new ClusteringConfiguration();
        }

        public class FileStorageConfiguration
        {
            /// <summary>
            /// Required. File storage provider type.
            /// LocalFileSystem: Files stored on server file system (requires FilesPath).
            /// GoogleCloudStorage: Files stored in Google Cloud Storage (requires ConnectionString with service account JSON path, and BucketName).
            /// AwsS3: Files stored in AWS S3 or S3-compatible storage (requires ConnectionString with access keys, and BucketName).
            /// AzureBlobStorage: Files stored in Azure Blob Storage (requires ConnectionString with storage account connection string, and BucketName as container name).
            /// </summary>
            public StorageProviderType Provider { get; set; } = StorageProviderType.LocalFileSystem;

            /// <summary>
            /// Optional. Connection string or credentials for cloud storage providers.
            /// LocalFileSystem: Not used.
            /// GoogleCloudStorage: Path to service account JSON file (e.g., "/run/secrets/gcs-sa.json").
            /// AwsS3: Format "AccessKey={key};SecretKey={secret};Region={region}" or "AccessKey={key};SecretKey={secret};ServiceUrl={url}" for S3-compatible endpoints.
            /// AzureBlobStorage: Azure Storage Account connection string (e.g., "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...").
            /// </summary>
            public string? ConnectionString { get; set; }

            /// <summary>
            /// Optional. Bucket/container name for cloud storage providers.
            /// LocalFileSystem: Not used.
            /// GoogleCloudStorage: GCS bucket name.
            /// AwsS3: S3 bucket name.
            /// AzureBlobStorage: Azure Blob container name.
            /// </summary>
            public string? BucketName { get; set; }
        }

        public class OpenTelemetryConfiguration
        {
            public OpenTelemetryMetricsConfiguration Metrics { get; set; } = null!;
            public OpenTelemetryTracingConfiguration Tracing { get; set; } = null!;

            public class OpenTelemetryTracingConfiguration
            {
                public bool Enabled { get; set; } = false;
                public string Url { get; set; } = null!;
                public double SampleRatio { get; set; } = 0.1;
            }

            public class OpenTelemetryMetricsConfiguration
            {
                public bool Enabled { get; set; } = false;
            }
        }
    }
}
