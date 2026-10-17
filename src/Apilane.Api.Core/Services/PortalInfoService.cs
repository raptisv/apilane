using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Common;
using Apilane.Common.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
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

        public PortalInfoService(
            ILogger<PortalInfoService> logger,
            IHttpClientFactory clientFactory,
            ApiConfiguration apiConfiguration)
        {
            _logger = logger;
            _clientFactory = clientFactory;
            _apiConfiguration = apiConfiguration;
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
            // The token is part of the address the Portal is asked at. An application token is a
            // GUID; anything else (empty, '..') is nobody's application and would be another address.
            if (string.IsNullOrWhiteSpace(authToken) || !Guid.TryParse(appToken, out _))
            {
                return false;
            }

            // Authorization must reflect the current token, role and collaborators. Caching a
            // grant here lets a revoked token retain access independently of the Portal session.
            return await UserOwnsApplicationInnerAsync(authToken, appToken);
        }

        private async Task<bool> UserOwnsApplicationInnerAsync(string authToken, string appToken)
        {
            // The installation key and the portal user's token travel in headers, never in the URL,
            // so they do not end up in request logs, proxies or browser histories.
            string url = $"{_apiConfiguration.PortalUrl.Trim('/')}/api/internal/applications/{Uri.EscapeDataString(appToken)}/access";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add(Globals.InstallationKeyHeaderName, _apiConfiguration.InstallationKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

            using var client = _clientFactory.CreateClient(HttpClientName);

            HttpResponseMessage response;

            try
            {
                response = await client.SendAsync(request);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                // Down, or slower than the client's timeout
                _logger.LogError(ex, $"Error UserOwnsApplication from Portal | {ex.Message}");
                throw PortalUnreachable();
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    // For example a 401 after the installation key was changed in the Portal only
                    _logger.LogError($"Error UserOwnsApplication from Portal | StatusCode {response.StatusCode} | ReasonPhrase {response.ReasonPhrase}");
                    throw PortalUnreachable();
                }

                var strReponse = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<bool>(strReponse);
            }
        }

        /// <summary>
        /// The answer when the Portal cannot be asked: the usual error body, saying why, instead of a general error.
        /// </summary>
        private static ApilaneException PortalUnreachable()
        {
            return new ApilaneException(AppErrors.SERVICE_UNAVAILABLE, "The API server could not reach the Portal");
        }

        public async Task<DBWS_Application> GetApplicationAsync(string appToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiConfiguration.PortalUrl.Trim('/')}/api/internal/applications/{Uri.EscapeDataString(appToken)}");
            request.Headers.Add(Globals.InstallationKeyHeaderName, _apiConfiguration.InstallationKey);

            using var client = _clientFactory.CreateClient(PortalInfoService.HttpClientName);

            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string mesage = $"Error getting app with token | StatusCode {response.StatusCode} | ReasonPhrase {response.ReasonPhrase}";
                _logger.LogError(mesage);
                throw new Exception(mesage);
            }

            var strReponse = await response.Content.ReadAsStringAsync();

            // The Portal answers 'null' for a token that is nobody's application
            var application = string.IsNullOrWhiteSpace(strReponse)
                ? null
                : JsonSerializer.Deserialize<DBWS_Application>(strReponse);

            return application
                ?? throw new ApilaneException(AppErrors.NOT_FOUND, "Application not found");
        }
    }
}
