using Apilane.Common.Enums;
using Apilane.Common.Utilities;
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
            // "Environment" is normally set in appsettings.{Environment}.json. When that file is absent
            // (e.g. containers configured purely through environment variables) fall back to
            // ASPNETCORE_ENVIRONMENT, and to Production when neither is set.
            Environment = configuration.GetValue<HostingEnvironment?>("Environment")
                ?? EnvVariables.GetEnvironment("ASPNETCORE_ENVIRONMENT", HostingEnvironment.Production);

            Url = configuration.GetValue<string>("Url") ?? throw Missing(nameof(Url));
            FilesPath = configuration.GetValue<string>("FilesPath") ?? throw Missing(nameof(FilesPath));
            FileStorage = configuration.GetSection("FileStorage").Get<FileStorageConfiguration>() ?? new FileStorageConfiguration();
            PortalUrl = configuration.GetValue<string>("PortalUrl") ?? throw Missing(nameof(PortalUrl));
            InstallationKey = configuration.GetValue<string>("InstallationKey") ?? throw Missing(nameof(InstallationKey));
            MinThreads = configuration.GetValue<int?>("MinThreads");
            InvalidFilesExtentions = configuration.GetSection("InvalidFilesExtentions").Get<List<string>>()
                ?? new List<string>() { ".exe", ".vbs", ".msi", ".jar", ".bat", ".cmd", ".vbe", ".js", ".jsp", ".lnk" };
            OpenTelemetry = configuration.GetSection("OpenTelemetry").Get<OpenTelemetryConfiguration>()
                ?? new OpenTelemetryConfiguration();
            OpenTelemetry.Metrics ??= new OpenTelemetryConfiguration.OpenTelemetryMetricsConfiguration();
            OpenTelemetry.Tracing ??= new OpenTelemetryConfiguration.OpenTelemetryTracingConfiguration();
            Clustering = configuration.GetSection("Clustering").Get<ClusteringConfiguration>() ?? new ClusteringConfiguration();
        }

        private static Exception Missing(string setting)
        {
            return new InvalidOperationException(
                $"Setting '{setting}' is required. Provide it in appsettings.json, appsettings.{{Environment}}.json or as an environment variable.");
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
                // When true (default), every finished span is also written as a Serilog log event
                // (carrying TraceId/SpanId/ParentId), so the request flow is logged.
                public bool LogSpans { get; set; } = true;
            }

            public class OpenTelemetryMetricsConfiguration
            {
                public bool Enabled { get; set; } = false;
            }
        }
    }
}
