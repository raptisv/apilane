using Apilane.Api.Core.Services.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using System.Diagnostics;
using static Apilane.Api.Core.Configuration.ApiConfiguration;

namespace Apilane.Api.Extensions
{
    public static class OpenTelemetryExtensions
    {
        private static class DiagnosticsConfig
        {
            public const string ServiceName = "Apilane.Api";
            public static ActivitySource ActivitySource = new ActivitySource(ServiceName);
        }

        /// <summary>
        /// Bridges finished OpenTelemetry spans into Serilog, so the request flow (including
        /// auto-instrumented spans such as ASP.NET Core, HttpClient and SqlClient) is visible
        /// as log entries in Graylog, correlated via TraceId/SpanId/ParentId (see Program.cs'
        /// Enrich.WithSpan()). Runs alongside the OTLP exporter, respecting the same sampler.
        /// </summary>
        private sealed class SpanLoggingProcessor : BaseProcessor<Activity>
        {
            public override void OnEnd(Activity activity)
            {
                if (!activity.Recorded)
                {
                    return;
                }

                var level = activity.Status == ActivityStatusCode.Error ? LogEventLevel.Warning : LogEventLevel.Information;

                Log.Logger
                    .ForContext("TraceId", activity.TraceId.ToString())
                    .ForContext("SpanId", activity.SpanId.ToString())
                    .ForContext("ParentId", activity.ParentSpanId == default ? null : activity.ParentSpanId.ToString())
                    .ForContext("SpanKind", activity.Kind.ToString())
                    .ForContext("SpanStatus", activity.Status.ToString())
                    .Write(level, "SPAN {SpanOperation} ({SpanDurationMs} ms)", activity.DisplayName, activity.Duration.TotalMilliseconds);
            }
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
                            metrics.ConfigureResource(resource => resource.AddService(DiagnosticsConfig.ServiceName));
                            metrics.AddMeter(MetricsService.MeterName);
                            // Framework + runtime metrics (request rates/durations, HTTP client, GC, thread pool, ...).
                            metrics.AddAspNetCoreInstrumentation();
                            metrics.AddHttpClientInstrumentation();
                            metrics.AddRuntimeInstrumentation();
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
                        {
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
                                });

                            if (config.Tracing.LogSpans)
                            {
                                tracerProviderBuilder.AddProcessor(new SpanLoggingProcessor());
                            }
                        });
                }
            }

            return services;
        }
    }
}
