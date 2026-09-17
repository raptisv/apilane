using Microsoft.Extensions.Configuration;
using System;

namespace Apilane.Portal.Models
{
    public class PortalConfiguration
    {
        public string Url { get; }
        public string FilesPath { get; }
        public string InstanceTitle { get; }
        public string InstallationKey { get; }
        public string AdminEmail { get; }
        public string ApiUrl { get; }
        public int? MinThreads { get; set; }
        public string? AuthCookieDomain { get; }
        public OpenTelemetryConfiguration OpenTelemetry { get; }

        public PortalConfiguration(IConfiguration configuration)
        {
            Url = configuration.GetValue<string>("Url") ?? throw new ArgumentNullException("Url");
            FilesPath = configuration.GetValue<string>("FilesPath") ?? throw new ArgumentNullException("FilesPath");
            InstanceTitle = configuration.GetValue<string>("InstanceTitle") ?? throw new ArgumentNullException("InstanceTitle");
            InstallationKey = configuration.GetValue<string>("InstallationKey") ?? throw new ArgumentNullException("InstallationKey");
            AdminEmail = configuration.GetValue<string>("AdminEmail") ?? throw new ArgumentNullException("AdminEmail");
            ApiUrl = configuration.GetValue<string>("ApiUrl") ?? throw new ArgumentNullException("ApiUrl");
            MinThreads = configuration.GetValue<int?>("MinThreads");
            AuthCookieDomain = configuration.GetValue<string>("AuthCookieDomain");
            OpenTelemetry = configuration.GetSection("OpenTelemetry").Get<OpenTelemetryConfiguration>() ?? new OpenTelemetryConfiguration();
        }

        public class OpenTelemetryConfiguration
        {
            public OpenTelemetryMetricsConfiguration Metrics { get; set; } = new();
            public OpenTelemetryTracingConfiguration Tracing { get; set; } = new();

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
