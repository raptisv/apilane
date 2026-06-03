using Apilane.Api.Core;
using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Abstractions.Metrics;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Services;
using Apilane.Api.Core.Services.Metrics;
using Apilane.Api.Core.Services.Storage;
using Apilane.Common.Abstractions;
using Apilane.Common.Models.Dto;
using Apilane.Common.Services;
using Apilane.Data.Abstractions;
using Apilane.Data.Repository.Factory;
using Apilane.Api.Filters;
using Apilane.Api.HostedServices;
using Apilane.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Serilog;
using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Apilane.Api.Extensions
{
    public static class ServiceDependencyInjection
    {
        public static IServiceCollection AddServices(
            this IServiceCollection services,
            ApiConfiguration apiConfiguration)
        {
            services
            // Singleton
                .AddSingleton<ApiConfiguration>((s) => apiConfiguration)
                .AddSingleton<ILogger>((s) => Log.Logger)
                .AddSingleton<IEmailService, EmailService>()
                .AddSingleton<IMetricsService, MetricsService>()                
                .AddSingleton<IHttpContextAccessor, HttpContextAccessor>()
                .AddSingleton<IPortalInfoService, PortalInfoService>()
                .AddSingleton<IApplicationService, ApplicationService>()
                .AddSingleton<IActionContextAccessor, ActionContextAccessor>()
                .AddSingleton<ITransactionScopeService, TransactionScopeService>()
                // Scoped (depend on IApplicationDataStoreFactory which is scoped)
                .AddScoped<IApplicationHelperService, ApplicationHelperService>()
                .AddScoped<IApplicationEmailService, ApplicationEmailService>()
                .AddScoped<IDataAPI, DataAPI>()
                .AddScoped<IFileAPI, FileAPI>()
                .AddScoped<IEmailAPI, EmailAPI>()
                .AddScoped<IStatsAPI, StatsAPI>()
                .AddScoped<ICustomAPI, CustomAPI>()
                .AddScoped<IAccountAPI, AccountAPI>()
                .AddScoped<IApplicationAPI, ApplicationAPI>()
                .AddScoped<IEntityHistoryAPI, EntityHistoryAPI>()
                .AddScoped<IApplicationNewAPI, ApplicationNewAPI>()
                .AddScoped<IQueryDataService, HttpQueryDataService>()
                .AddScoped<IApplicationDataService, ApplicationDataService>()
                .AddScoped<IApplicationBuilderService, ApplicationBuilderService>()
                .AddScoped<IApplicationDataStoreFactory>((serviceProvider) =>
                {
                    var applicationService = serviceProvider.GetRequiredService<IApplicationService>();
                    var queryDataService = serviceProvider.GetRequiredService<IQueryDataService>();
                    var optlTracer = apiConfiguration.OpenTelemetry.Tracing.Enabled ? serviceProvider.GetRequiredService<Tracer>() : null;

                    var applicationDbInfoTask = applicationService.GetDbInfoAsync(queryDataService.AppToken);
                    return new ApplicationDataStoreFactory(
                        new Lazy<ValueTask<ApplicationDbInfoDto>>(applicationDbInfoTask),
                        optlTracer);
                })
                // Filters
                .AddScoped<ApplicationLogActionFilter>()
                .AddScoped<ApiExceptionFilter>()
                // Open telemetry
                .AddOpenTelemetry(apiConfiguration.OpenTelemetry)
                // Http client
                .AddHttpClient(PortalInfoService.HttpClientName, c =>
                {
                    c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    c.Timeout = TimeSpan.FromSeconds(5);
                });

            services.AddSingleton<ICloudStorageProvider>((sp) =>
            {
                var provider = apiConfiguration.FileStorage.Provider;
                return provider switch
                {
                    StorageProviderType.LocalFileSystem => new LocalFileSystemProvider(apiConfiguration),
                    StorageProviderType.GoogleCloudStorage => new GoogleCloudStorageProvider(apiConfiguration.FileStorage),
                    StorageProviderType.AwsS3 => new AwsS3StorageProvider(apiConfiguration.FileStorage),
                    StorageProviderType.AzureBlobStorage => new AzureBlobStorageProvider(apiConfiguration.FileStorage),
                    _ => throw new InvalidOperationException($"Unknown storage provider: {provider}")
                };
            });

            // Hosted services

            services
                .AddHostedService<AppLifetimeHostedService>();

            return services;
        }
    }
}
