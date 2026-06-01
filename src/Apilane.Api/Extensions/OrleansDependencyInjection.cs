using Apilane.Api.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using System;
using System.Net;

namespace Apilane.Api.Extensions
{
    public static class OrleansDependencyInjection
    {
        public static IServiceCollection AddOrleans(
            this WebApplicationBuilder builder,
            ApiConfiguration appConfig)
        {
            // Register Orleans
            builder.Host.UseOrleans(siloBuilder =>
            {
                siloBuilder.Services.AddSerializer(sb =>
                {
                    sb.AddJsonSerializer(
                        isSupported: type =>
                            type.Namespace != null && type.Namespace.StartsWith("Apilane")
                    );
                });

                siloBuilder
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("MemoryGrainStorage")
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = appConfig.Clustering.ClusterId;
                    options.ServiceId = appConfig.Clustering.ServiceId;
                })
                .Configure<MessagingOptions>(options =>
                {
                    options.ResponseTimeout = TimeSpan.FromSeconds(5);
                    options.ResponseTimeoutWithDebugger = TimeSpan.FromMinutes(60);
                })
                .Configure<ClusterMembershipOptions>(options =>
                {
                    options.DefunctSiloCleanupPeriod = TimeSpan.FromHours(1);
                    options.DefunctSiloExpiration = TimeSpan.FromHours(1);
                    options.IAmAliveTablePublishTimeout = TimeSpan.FromMinutes(1);
                })
                .Configure<DeploymentLoadPublisherOptions>(options =>
                {
                    // The statistics are currently used by ActivationCountPlacementDirector that tries to achieve a balanced distribution of grain activations across silos.
                    // If you are not using ActivationCountPlacement policy for your grains classes, then decreasing the interval will have no negative effect on behavior of the cluster.
                    options.DeploymentLoadPublisherRefreshTime = TimeSpan.FromMinutes(1);
                })
                .Configure<EndpointOptions>(options =>
                {
                    options.AdvertisedIPAddress = IPAddress.Loopback;
                    options.SiloPort = appConfig.Clustering.SiloPort;
                    options.GatewayPort = appConfig.Clustering.GatewayPort;
                })
                .ConfigureClustering(appConfig.Clustering)
                .UseDashboard(options =>
                {
                    options.CounterUpdateIntervalMs = 5000;
                    options.Port = appConfig.Clustering.DashboardPort;
                });
            });

            return builder.Services;
        }

        private static ISiloBuilder ConfigureClustering(
            this ISiloBuilder builder,
            ClusteringConfiguration config)
        {
            switch (config.Type)
            {
                case ClusteringType.Redis:
                    if (config.Redis?.ConnectionString != null)
                    {
                        builder.UseRedisClustering(config.Redis.ConnectionString);
                    }
                    else
                    {
                        throw new InvalidOperationException("Redis clustering selected but no connection string provided in Clustering:Redis:ConnectionString");
                    }
                    break;

                case ClusteringType.AdoNet:
                    if (config.AdoNet?.ConnectionString != null)
                    {
                        builder.UseAdoNetClustering(options =>
                        {
                            options.ConnectionString = config.AdoNet.ConnectionString;
                            options.Invariant = config.AdoNet.Invariant;
                        });
                    }
                    else
                    {
                        throw new InvalidOperationException("AdoNet clustering selected but no connection string provided in Clustering:AdoNet:ConnectionString");
                    }
                    break;

                case ClusteringType.Localhost:
                default:
                    builder.UseLocalhostClustering();
                    break;
            }

            return builder;
        }
    }
}
