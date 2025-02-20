﻿using Apilane.Common.Abstractions;
using Apilane.Common.Services;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Apilane.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Net.Http.Headers;

namespace Apilane.Portal.Extensions
{
    public static class ServiceDependencyInjection
    {
        public static IServiceCollection AddServices(
            this IServiceCollection services,
            PortalConfiguration portalConfiguration)
        {
            services
            // Singleton
            .AddSingleton<PortalConfiguration>((s) => portalConfiguration)
            .AddSingleton<ILogger>((s) => Log.Logger)
            .AddSingleton<IEmailService, EmailService>()
            .AddSingleton<IApiHttpService, ApiHttpService>()
            // Scoped
            .AddScoped<IPortalSettingsService, PortalSettingsService>()
            .AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, Controllers.AccountController.AppClaimsPrincipalFactory>()
            // Http client
            .AddHttpClient("Api", c =>
            {
                c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });

            return services;
        }
    }
}
