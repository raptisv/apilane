using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1;
using Apilane.Portal.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Apilane.Portal.Extensions
{
    public static class BootstrapDependencyInjection
    {
        public static IServiceCollection AddPortalBootstrap(this IServiceCollection services)
        {
            return services.AddScoped<IPortalBootstrapService, PortalBootstrapService>();
        }

        public static IApplicationBuilder UsePortalBootstrap(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                var isApi = context.Request.Path.StartsWithSegments("/api");
                var isDocs = context.Request.Path.StartsWithSegments("/swagger");
                var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
                var isSetup = action?.ControllerTypeInfo.AsType() == typeof(BootstrapController);
                var isPublicInstance = action?.ControllerTypeInfo.AsType() == typeof(InstanceController)
                    && HttpMethods.IsGet(context.Request.Method);

                if ((isApi || isDocs) && !isSetup && !isPublicInstance
                    && await context.RequestServices.GetRequiredService<IPortalBootstrapService>().IsRequiredAsync())
                {
                    context.Response.Headers.CacheControl = "no-store";
                    await PortalApiErrors.WriteAsync(context, StatusCodes.Status409Conflict,
                        "SETUP_REQUIRED", "Set the first administrator's password before using this instance.");
                    return;
                }

                await next();
            });
        }
    }
}
