using Apilane.Api.Abstractions;
using Apilane.Api.Grains;
using Apilane.Common.Models;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Threading.Tasks;

namespace Apilane.Api.Services
{
    public class ApplicationService : IApplicationService
    {
        private readonly ILogger<ApplicationService> _logger;
        private readonly IClusterClient _clusterClient;

        public ApplicationService(
            ILogger<ApplicationService> logger,
            IClusterClient clusterClient)
        {
            _logger = logger;
            _clusterClient = clusterClient;
        }

        public virtual async Task<DBWS_Application> GetAsync(string appToken)
        {
            var grainRef = _clusterClient.GetGrain<IApplicationGrain>(new Guid(appToken));
            return await grainRef.GetAsync();
        }
    }
}
