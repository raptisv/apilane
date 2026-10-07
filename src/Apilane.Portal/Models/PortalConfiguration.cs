using Microsoft.Extensions.Configuration;
using System;

namespace Apilane.Portal.Models
{
    public class PortalConfiguration
    {
        public string Url { get; }
        /// <summary>
        /// Trusted browser-facing origin for links in e-mails. A concrete listening URL is the
        /// development fallback; wildcard listeners need an explicit PublicUrl before mailing.
        /// </summary>
        public Uri? PublicUrl { get; }
        public string FilesPath { get; }
        public string InstanceTitle { get; }
        /// <summary>
        /// Seeds the installation key stored in the Portal database on the first start, and is checked
        /// for the startup warning. It is not used at runtime: the key in force, in both directions, is
        /// the stored one an administrator changes under Instance > Settings.
        /// </summary>
        public string InstallationKey { get; }
        public string ApiUrl { get; }
        public int? MinThreads { get; set; }
        public string? AuthCookieDomain { get; }
        public OpenTelemetryConfiguration OpenTelemetry { get; }

        public PortalConfiguration(IConfiguration configuration)
        {
            Url = configuration.GetValue<string>("Url") ?? throw new ArgumentNullException("Url");
            var publicUrl = configuration.GetValue<string>("PublicUrl");
            PublicUrl = GetPublicOrigin(string.IsNullOrWhiteSpace(publicUrl) ? Url : publicUrl);
            if (!string.IsNullOrWhiteSpace(publicUrl) && PublicUrl is null)
            {
                throw new ArgumentException("PublicUrl must be an absolute http or https origin with a concrete host, without credentials, a path, a query or a fragment.", "PublicUrl");
            }
            FilesPath = configuration.GetValue<string>("FilesPath") ?? throw new ArgumentNullException("FilesPath");
            InstanceTitle = configuration.GetValue<string>("InstanceTitle") ?? throw new ArgumentNullException("InstanceTitle");
            InstallationKey = configuration.GetValue<string>("InstallationKey") ?? throw new ArgumentNullException("InstallationKey");
            ApiUrl = configuration.GetValue<string>("ApiUrl") ?? throw new ArgumentNullException("ApiUrl");
            MinThreads = configuration.GetValue<int?>("MinThreads");
            AuthCookieDomain = configuration.GetValue<string>("AuthCookieDomain");
            OpenTelemetry = configuration.GetSection("OpenTelemetry").Get<OpenTelemetryConfiguration>() ?? new OpenTelemetryConfiguration();
        }

        private static Uri? GetPublicOrigin(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || uri.Host is "0.0.0.0" or "::" or "[::]" or "*" or "+")
            {
                return null;
            }

            return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
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
