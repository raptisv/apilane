using Apilane.Api.Services.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using static Apilane.Api.Configuration.ApiConfiguration;

namespace Apilane.Web.Api.Extensions
{
    public static class OpenTelemetryExtensions
    {
        private static class DiagnosticsConfig
        {
            public const string ServiceName = "Apilane.Web.Api";
            public static ActivitySource ActivitySource = new ActivitySource(ServiceName);
        }

        public static IServiceCollection AddOpenTelemetry(
            this IServiceCollection services,
            OpenTelemetryConfiguration config)
        {
            if (config.Metrics.Enabled || config.Tracing.Enabled)
            {
                services.AddSingleton(TracerProvider.Default.GetTracer(DiagnosticsConfig.ServiceName));

                var optlBuilder = services.AddOpenTelemetry();

                if (config.Metrics.Enabled)
                {
                    optlBuilder = optlBuilder
                        .WithMetrics(metrics =>
                        {
                            metrics.AddMeter(MetricsService.MeterName);
                            metrics.AddPrometheusExporter();
                            metrics.AddView(
                                instrumentName: "apilane_api_data_duration",
                                new ExplicitBucketHistogramConfiguration
                                {
                                    Boundaries = new double[] { 0.001, 0.002, 0.003, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10 }
                                });
                        });
                }

                if (config.Tracing.Enabled)
                {
                    optlBuilder = optlBuilder
                        .WithTracing(tracerProviderBuilder =>
                            tracerProviderBuilder
                                .AddSource(DiagnosticsConfig.ActivitySource.Name)
                                .ConfigureResource(resource => resource.AddService(DiagnosticsConfig.ServiceName))
                                .SetSampler(new TraceIdRatioBasedSampler(config.Tracing.SampleRatio))
                                .AddAspNetCoreInstrumentation(options =>
                                {
                                    options.Filter = context =>
                                    {
                                        // Exclude /health, /metrics and swagger requests
                                        return !(context.Request.Path.StartsWithSegments("/health")
                                            || context.Request.Path.StartsWithSegments("/metrics")
                                            || context.Request.Path.StartsWithSegments("/swagger"));
                                    };
                                })
                                .AddHttpClientInstrumentation()
                                .AddSqlClientInstrumentation()
                                .AddOtlpExporter(options =>
                                {
                                    options.Endpoint = new System.Uri(config.Tracing.Url);
                                }));
                }
            }

            return services;
        }
    }
}
