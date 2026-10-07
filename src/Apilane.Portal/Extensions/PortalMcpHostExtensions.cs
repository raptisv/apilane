using Apilane.Portal.Abstractions;
using Apilane.Portal.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Apilane.Portal.Extensions
{
    public static class PortalMcpHostExtensions
    {
        public static IServiceCollection AddPortalMcpHost(this IServiceCollection services)
        {
            services.AddScoped<IMcpPortalStore, PortalMcpStore>();
            services.AddScoped<IMcpOperationPolicy, PortalMcpOperationPolicy>();
            return services.AddPortalMcp();
        }
    }
}
