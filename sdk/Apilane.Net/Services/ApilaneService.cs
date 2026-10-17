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
        private readonly Func<HttpClient> _getHttpClient;
        private readonly ApilaneConfiguration _config;

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

        /// <summary>
        /// A service that sends every request with the given client. The client also gets the application
        /// token as a default header, for the requests its owner sends with it.
        /// </summary>
        internal ApilaneService(
            HttpClient httpClient,
            ApilaneConfiguration config) : this(() => httpClient, config)
        {
            if (!httpClient.DefaultRequestHeaders.Contains(_applicationTokenHeaderName))
            {
                httpClient.DefaultRequestHeaders.Add(_applicationTokenHeaderName, _config.ApplicationToken);
            }
        }

        /// <summary>
        /// A service that takes a client from the factory for each request, so that the factory's handler
        /// rotation (and with it DNS changes) applies to a service that lives as long as the application.
        /// </summary>
        internal ApilaneService(
            IHttpClientFactory httpClientFactory,
            string httpClientName,
            ApilaneConfiguration config) : this(() => httpClientFactory.CreateClient(httpClientName), config)
        {
        }

        private ApilaneService(
            Func<HttpClient> getHttpClient,
            ApilaneConfiguration config)
        {
            _getHttpClient = getHttpClient;
            _config = config;

            if (string.IsNullOrWhiteSpace(config.ApplicationApiUrl))
            {
                throw new Exception("Apilane api url is required");
            }

            if (string.IsNullOrWhiteSpace(config.ApplicationToken))
            {
                throw new Exception("Apilane application token is required");
            }
        }

        /// <summary>
        /// Sends a request with the application token header.
        /// </summary>
        private Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequest, CancellationToken cancellationToken)
        {
            httpRequest.Headers.Add(_applicationTokenHeaderName, _config.ApplicationToken);
            return _getHttpClient().SendAsync(httpRequest, cancellationToken);
        }

        public async Task<Either<long, ApilaneError>> HealthCheckAsync(
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{_config.ApplicationApiUrl}/Health/Liveness"))
            {
                var response = await SendAsync(httpRequest, cancellationToken);
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
            return $"{_config.ApplicationApiUrl.TrimEnd('/')}/api/Email/RequestConfirmation?AppToken={Uri.EscapeDataString(_config.ApplicationToken)}&Email={Uri.EscapeDataString(email)}";
        }

        public string UrlFor_Email_ForgotPassword(string email)
        {
            return $"{_config.ApplicationApiUrl.TrimEnd('/')}/api/Email/ForgotPassword?AppToken={Uri.EscapeDataString(_config.ApplicationToken)}&Email={Uri.EscapeDataString(email)}";
        }

        /// <summary>
        /// Applies authentication to an outgoing request. If the request is configured for signing
        /// (<see cref="ApilaneRequestBase{TBuilder}.WithSigning"/>), it is signed with HMAC and the
        /// token is never sent; otherwise the bearer token (if any) is attached.
        /// </summary>
        private async Task ApplyAuthAsync<TBuilder>(HttpRequestMessage httpRequest, ApilaneRequestBase<TBuilder> request) where TBuilder : ApilaneRequestBase<TBuilder>
        {
            if (request.HasSigning(out var keyId, out var secret))
            {
                var keyIdStr = keyId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

                var body = httpRequest.Content is null
                    ? Array.Empty<byte>()
                    : await httpRequest.Content.ReadAsByteArrayAsync();

                var pathAndQuery = httpRequest.RequestUri!.PathAndQuery;
                var canonical = RequestSigner.BuildCanonicalString(keyIdStr, httpRequest.Method.Method, pathAndQuery, timestamp, body);
                var signature = RequestSigner.ComputeSignature(secret, canonical);

                httpRequest.Headers.Add(RequestSigner.KeyIdHeader, keyIdStr);
                httpRequest.Headers.Add(RequestSigner.TimestampHeader, timestamp);
                httpRequest.Headers.Add(RequestSigner.SignatureHeader, signature);
                return;
            }

            if (request.HasAuthToken(out var authorizationToken))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
            }
        }
    }
}
