using Apilane.Api.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Web.Api.HealthChecks
{
    public class PortalConnectivityHealthCheck : IHealthCheck
    {
        private readonly IPortalInfoService _portalInfoService;

        public PortalConnectivityHealthCheck(
            IPortalInfoService portalInfoService)
        {
            _portalInfoService = portalInfoService;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await _portalInfoService.IsPortalHealhyAsync();

                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(description: ex.Message, exception: ex, data: new Dictionary<string, object>() { { "error", ex.Message } });
            }
        }
    }
}
