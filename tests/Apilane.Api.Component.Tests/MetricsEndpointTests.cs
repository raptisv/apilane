using Apilane.Api.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Threading.Tasks;
using Xunit;
using static Apilane.Api.Core.Configuration.ApiConfiguration;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// The API has to start with every combination of the metrics and tracing switches, and serve /metrics
    /// only when metrics are on. Without metrics no meter provider is registered, and asking for it, as the
    /// Prometheus scrape endpoint does, throws and ends the process at startup. Each test builds its own host
    /// from the configuration object, so nothing here reads or sets a process-wide setting that the shared API
    /// host of the other test classes would see.
    /// </summary>
    public class MetricsEndpointTests
    {
        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(false, true)]
        public async Task Metrics_Endpoint_Should_Be_Served_Only_When_Metrics_Are_Enabled(bool metricsEnabled, bool tracingEnabled)
        {
            var config = new OpenTelemetryConfiguration
            {
                Metrics = new OpenTelemetryConfiguration.OpenTelemetryMetricsConfiguration { Enabled = metricsEnabled },
                Tracing = new OpenTelemetryConfiguration.OpenTelemetryTracingConfiguration
                {
                    Enabled = tracingEnabled,
                    Url = "http://localhost:4317",
                    SampleRatio = 0,
                    LogSpans = false
                }
            };

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddOpenTelemetry(config);

            await using var app = builder.Build();

            // The same call as in Program.cs, in front of a last handler that answers like the API does for an
            // address nobody serves.
            app.UseMetricsEndpoint(config);
            app.Run(context =>
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return Task.CompletedTask;
            });

            await app.StartAsync();

            try
            {
                using var response = await app.GetTestClient().GetAsync("/metrics");

                Assert.Equal(metricsEnabled ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);

                if (metricsEnabled)
                {
                    Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
                    Assert.Contains("target_info", await response.Content.ReadAsStringAsync());
                }
            }
            finally
            {
                await app.StopAsync();
            }
        }
    }
}
