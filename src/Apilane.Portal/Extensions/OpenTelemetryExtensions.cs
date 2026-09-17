using Apilane.Portal.Models;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using System;
using System.Diagnostics;

namespace Apilane.Portal.Extensions
{
    public static class OpenTelemetryExtensions
    {
        private static class DiagnosticsConfig
        {
            public const string ServiceName = "Apilane.Portal";
            public static ActivitySource ActivitySource = new ActivitySource(ServiceName);
        }

        /// <summary>
        /// Bridges finished OpenTelemetry spans into Serilog, so the request flow (including
        /// auto-instrumented spans such as ASP.NET Core, HttpClient and EF Core) is visible
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
            PortalConfiguration.OpenTelemetryConfiguration config)
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
                            // Framework + runtime metrics (request rates/durations, HTTP client, GC, thread pool, ...).
                            metrics.AddAspNetCoreInstrumentation();
                            metrics.AddHttpClientInstrumentation();
                            metrics.AddRuntimeInstrumentation();
                            metrics.AddPrometheusExporter();
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
                                        // Exclude /health and /metrics requests
                                        return !(context.Request.Path.StartsWithSegments("/health")
                                            || context.Request.Path.StartsWithSegments("/metrics"));
                                    };
                                })
                                .AddHttpClientInstrumentation()
                                .AddEntityFrameworkCoreInstrumentation()
                                .AddOtlpExporter(options =>
                                {
                                    options.Endpoint = new Uri(config.Tracing.Url);
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
