using Apilane.Common.Enums;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace Apilane.Api.Core.Configuration
{
    public class ApiConfiguration
    {
        public HostingEnvironment Environment { get; set; }
        public string Url { get; set; }
        public string FilesPath { get; set; }
        public string PortalUrl { get; set; }
        public string InstallationKey { get; set; }
        public int? MinThreads { get; set; }
        public List<string> InvalidFilesExtentions { get; set; }
        public OpenTelemetryConfiguration OpenTelemetry { get; set; }
        public OrleansConfiguration Orleans { get; set; }

        public ApiConfiguration(IConfiguration configuration)
        {
            Environment = configuration.GetValue<HostingEnvironment>("Environment");
            Url = configuration.GetValue<string>("Url") ?? throw new ArgumentNullException(nameof(Url));
            FilesPath = configuration.GetValue<string>("FilesPath") ?? throw new ArgumentNullException(nameof(FilesPath));
            PortalUrl = configuration.GetValue<string>("PortalUrl") ?? throw new ArgumentNullException(nameof(PortalUrl));
            InstallationKey = configuration.GetValue<string>("InstallationKey") ?? throw new ArgumentNullException(nameof(InstallationKey));
            MinThreads = configuration.GetValue<int?>("MinThreads") ?? throw new ArgumentNullException(nameof(MinThreads));
            InvalidFilesExtentions = configuration.GetSection("InvalidFilesExtentions").Get<List<string>>() ?? throw new ArgumentNullException(nameof(InvalidFilesExtentions));
            OpenTelemetry = configuration.GetSection("OpenTelemetry").Get<OpenTelemetryConfiguration>() ?? throw new ArgumentNullException(nameof(OpenTelemetry));
            Orleans = configuration.GetSection("Orleans").Get<OrleansConfiguration>() ?? throw new ArgumentNullException(nameof(Orleans));
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

        public class OrleansConfiguration
        {
            public ClusterConfiguration Cluster { get; set; } = null!;
            public class ClusterConfiguration
            {
                public int SiloPort { get; set; } = 11111;
                public int GatewayPort { get; set; } = 30000;
                public int DashboardPort { get; set; } = 8080; 
            }
        }
    }
}
