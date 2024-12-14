using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Common.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Services
{
    public class PortalInfoService : IPortalInfoService
    {
        public const string HttpClientName = "Portal";

        private readonly ILogger<PortalInfoService> _logger;
        private readonly IHttpClientFactory _clientFactory;
        private readonly ApiConfiguration _apiConfiguration;
        private readonly IMemoryCache _memoryCache;

        public PortalInfoService(
            ILogger<PortalInfoService> logger,
            IHttpClientFactory clientFactory,
            ApiConfiguration apiConfiguration,
            IMemoryCache memoryCache)
        {
            _logger = logger;
            _clientFactory = clientFactory;
            _apiConfiguration = apiConfiguration;
            _memoryCache = memoryCache;
        }

        public async Task IsPortalHealhyAsync()
        {
            string url = $"{_apiConfiguration.PortalUrl.Trim('/')}/health/liveness";

            Console.WriteLine(url);

            var request = new HttpRequestMessage(HttpMethod.Get, url);

            using var client = _clientFactory.CreateClient(HttpClientName);

            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error PortalCheckAsync from Portal | StatusCode {response.StatusCode} | ReasonPhrase {response.ReasonPhrase}");
            }
        }

        public async Task<bool> UserOwnsApplicationAsync(string authToken, string appToken)
        {
            var cacheKey = $"{authToken}_{appToken}";

            if (!_memoryCache.TryGetValue(cacheKey, out bool userOwnsApplication))
            {
                userOwnsApplication = await UserOwnsApplicationInnerAsync(authToken, appToken);

                _memoryCache.Set(cacheKey, userOwnsApplication, new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(15)));
            }

            return userOwnsApplication;
        }

        private async Task<bool> UserOwnsApplicationInnerAsync(string authToken, string appToken)
        {
            string url = $"{_apiConfiguration.PortalUrl.Trim('/')}/Info/UserOwnsApplication?authToken={authToken}&appToken={appToken}&key={_apiConfiguration.InstallationKey}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);

            using var client = _clientFactory.CreateClient(HttpClientName);

            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error UserOwnsApplication from Portal | StatusCode {response.StatusCode} | ReasonPhrase {response.ReasonPhrase}");
            }

            var strReponse = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<bool>(strReponse);
        }

        public async Task<DBWS_Application> GetApplicationAsync(string appToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiConfiguration.PortalUrl}/Info/GetApplication?appToken={appToken}&key={_apiConfiguration.InstallationKey}");

            using var client = _clientFactory.CreateClient(PortalInfoService.HttpClientName);

            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string mesage = $"Error getting app with token | StatusCode {response.StatusCode} | ReasonPhrase {response.ReasonPhrase}";
                _logger.LogError(mesage);
                throw new Exception(mesage);
            }

            var strReponse = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(strReponse))
            {
                throw new Exception($"Application with token '{appToken}' not found");
            }

            return JsonSerializer.Deserialize<DBWS_Application>(strReponse)
                ?? throw new Exception($"Application with token '{appToken}' response was empty");
        }
    }
}
