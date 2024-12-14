using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Grains;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Services
{
    public class ApplicationService : IApplicationService
    {
        private readonly ILogger<ApplicationService> _logger;
        private readonly ApiConfiguration _apiConfiguration;
        private readonly IClusterClient _clusterClient;
        private readonly IMemoryCache _memoryCache;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        private IApplicationService _selfObserverReference;

        public ApplicationService(
            ILogger<ApplicationService> logger,
            ApiConfiguration apiConfiguration,
            IClusterClient clusterClient,
            IMemoryCache memoryCache)
        {
            _logger = logger;
            _apiConfiguration = apiConfiguration;
            _clusterClient = clusterClient;
            _memoryCache = memoryCache;

            _selfObserverReference = _clusterClient.CreateObjectReference<IApplicationService>(this);
        }

        public async ValueTask<ApplicationDbInfoDto> GetDbInfoAsync(string appToken)
        {
            var application = await GetAsync(appToken);
            return application.ToDbInfo(_apiConfiguration.FilesPath);
        }

        public async Task<DBWS_Application> GetAsync(string appToken)
        {
            var cacheKey = appToken;

            if (!_memoryCache.TryGetValue(cacheKey, out DBWS_Application? application) ||
                application is null)
            {
                _semaphore.Wait();

                try
                {
                    if (!_memoryCache.TryGetValue(cacheKey, out application) ||
                        application is null)
                    {
                        var grainRef = _clusterClient.GetGrain<IApplicationGrain>(new Guid(appToken));
                        application = await grainRef.SubscribeAndGetAsync(_selfObserverReference);

                        _memoryCache.Set(cacheKey, application, new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(DateTimeOffset.UtcNow.AddSeconds(30)));
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            return application;
        }

        public Task ApplicationChangedAsync(string appToken)
        {
            _logger.LogInformation($"'{appToken}' | Observed application changed event");

            // Clear memory cache
            var cacheKey = appToken;
            _memoryCache.Remove(cacheKey);
            return Task.CompletedTask;
        }
    }
}
