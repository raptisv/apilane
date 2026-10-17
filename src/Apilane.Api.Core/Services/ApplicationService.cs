using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Grains;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Services
{
    public class ApplicationService : IApplicationService
    {
        private readonly ILogger<ApplicationService> _logger;
        private readonly ApiConfiguration _apiConfiguration;
        private readonly IClusterClient _clusterClient;
        private readonly IMemoryCache _memoryCache;
        private readonly ConcurrentDictionary<string, Lazy<Task<DBWS_Application>>> _loads = new();

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

            if (_memoryCache.TryGetValue(cacheKey, out DBWS_Application? application) &&
                application is not null)
            {
                return application;
            }

            // An application token is a GUID, and a token found to be nobody's a moment ago is not asked again
            if (!Guid.TryParse(appToken, out var applicationId) ||
                _memoryCache.TryGetValue(GetNotFoundCacheKey(appToken), out _))
            {
                throw new ApilaneException(AppErrors.NOT_FOUND, "Application not found");
            }

            // The requests for an application that is loading wait for that one load, without blocking a thread.
            // Loads of different applications do not wait for each other.
            var load = _loads.GetOrAdd(cacheKey, _ => new Lazy<Task<DBWS_Application>>(() => LoadAsync(cacheKey, applicationId)));

            try
            {
                return await load.Value;
            }
            finally
            {
                // The answer is cached by now: the next request reads it, or starts a new load
                _loads.TryRemove(new KeyValuePair<string, Lazy<Task<DBWS_Application>>>(cacheKey, load));
            }
        }

        private async Task<DBWS_Application> LoadAsync(string appToken, Guid applicationId)
        {
            var grainRef = _clusterClient.GetGrain<IApplicationGrain>(applicationId);
            var application = await grainRef.SubscribeAndGetAsync(_selfObserverReference);

            if (application is null)
            {
                // Without this, every request with an unknown token would reach the Portal
                _memoryCache.Set(GetNotFoundCacheKey(appToken), true, new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(DateTimeOffset.UtcNow.AddSeconds(30)));

                throw new ApilaneException(AppErrors.NOT_FOUND, "Application not found");
            }

            _memoryCache.Set(appToken, application, new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(DateTimeOffset.UtcNow.AddSeconds(30)));

            return application;
        }

        private static string GetNotFoundCacheKey(string appToken)
        {
            return $"{appToken}|not-found";
        }

        public Task ApplicationChangedAsync(string appToken)
        {
            _logger.LogInformation($"'{appToken}' | Observed application changed event");

            // Clear memory cache
            var cacheKey = appToken;
            _memoryCache.Remove(cacheKey);
            _memoryCache.Remove(GetNotFoundCacheKey(appToken));
            return Task.CompletedTask;
        }
    }
}
