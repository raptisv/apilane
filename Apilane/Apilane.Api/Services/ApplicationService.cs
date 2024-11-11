using Apilane.Api.Abstractions;
using Apilane.Api.Grains;
using Apilane.Common.Models;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly IMemoryCache _memoryCache;

        public ApplicationService(
            ILogger<ApplicationService> logger,
            IClusterClient clusterClient,
            IMemoryCache memoryCache)
        {
            _logger = logger;
            _clusterClient = clusterClient;
            _memoryCache = memoryCache;
        }

        public virtual async Task<DBWS_Application> GetAsync(string appToken)
        {
            var cacheKey = appToken;

            if (!_memoryCache.TryGetValue(cacheKey, out DBWS_Application? application) ||
                application is null)
            {
                var grainRef = _clusterClient.GetGrain<IApplicationGrain>(new Guid(appToken));
                application = await grainRef.GetAsync();

                _memoryCache.Set(cacheKey, application, new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(DateTimeOffset.UtcNow.AddSeconds(5)));
            }

            return application;
        }
    }
}
