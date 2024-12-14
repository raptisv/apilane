using Apilane.Net.Abstractions;
using Apilane.Net.JsonConverters;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using Apilane.Net.Utilities;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Net.Services
{
    public sealed partial class ApilaneService : IApilaneService
    {
        private readonly string _applicationTokenHeaderName = "x-application-token";
        private readonly HttpClient _httpClient;
        private readonly ApilaneConfiguration _config;
        private readonly IApilaneAuthTokenProvider? _apilaneAuthTokenProvider;

        public static readonly JsonSerializerOptions JsonDeserializerSettings = new JsonSerializerOptions()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            AllowTrailingCommas = true,
            PropertyNamingPolicy = null,
            WriteIndented = false,
            Converters =
            {
                new NumericToIntegerConverter(),
                new NumericToNullIntegerConverter(),
                new NumericToLongConverter(),
                new NumericToNullLongConverter()
            }
        };

        internal ApilaneService(
            HttpClient httpClient,
            ApilaneConfiguration config,
            IApilaneAuthTokenProvider? apilaneAuthTokenProvider = null)
        {
            _httpClient = httpClient;
            _config = config;
            _apilaneAuthTokenProvider = apilaneAuthTokenProvider;

            if (string.IsNullOrWhiteSpace(config.ApplicationApiUrl))
            {
                throw new Exception("Apilane api url is required");
            }

            if (string.IsNullOrWhiteSpace(config.ApplicationToken))
            {
                throw new Exception("Apilane application token is required");
            }

            // Add application token header
            if (!_httpClient.DefaultRequestHeaders.Contains(_applicationTokenHeaderName))
            {
                _httpClient.DefaultRequestHeaders.Add(_applicationTokenHeaderName, _config.ApplicationToken);
            }
        }

        public async Task<Either<long, ApilaneError>> HealthCheckAsync(
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{_config.ApplicationApiUrl}/Health/Liveness"))
            {
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new ApilaneError()
                    {
                        Code = ValidationError.ERROR,
                        Message = jsonString
                    };
                }

                return 0;
            }
        }

        public string UrlFor_Account_Manage_ForgotPassword()
        {
            return $"{_config.ApplicationApiUrl.TrimEnd('/')}/App/{_config.ApplicationToken}/Account/Manage/ForgotPassword";
        }

        public string UrlFor_Email_RequestConfirmation(string email)
        {
            return $"{_config.ApplicationApiUrl.TrimEnd('/')}/api/Email/RequestConfirmation?AppToken={_config.ApplicationToken}&Email={email}";
        }

        public string UrlFor_Email_ForgotPassword(string email)
        {
            return $"{_config.ApplicationApiUrl.TrimEnd('/')}/api/Email/ForgotPassword?AppToken={_config.ApplicationToken}&Email={email}";
        }

        private async Task<string?> GetAuthTokenAsync<TBuilder>(ApilaneRequestBase<TBuilder> request) where TBuilder : ApilaneRequestBase<TBuilder>
        {
            // Request auth token is a priority
            if (request.HasAuthToken(out var requestAuthToken))
            {
                return requestAuthToken;
            }
            else
            {
                // Else check for global authtoken provider
                if (_apilaneAuthTokenProvider is not null)
                {
                    return await _apilaneAuthTokenProvider.GetAuthTokenAsync();
                }
            }
            
            return null;
        }
    }
}
