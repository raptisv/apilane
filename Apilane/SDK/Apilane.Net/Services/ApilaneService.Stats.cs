using Apilane.Net.Abstractions;
using Apilane.Net.Extensions;
using Apilane.Net.Models.Data;
using Apilane.Net.Request;
using Apilane.Net.Utilities;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Net.Services
{
    public sealed partial class ApilaneService : IApilaneService
    {
        public async Task<Either<string, ApilaneError>> GetStatsAggregateAsync(
            StatsAggregateRequest apiRequest,
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                var authorizationToken = await GetAuthTokenAsync(apiRequest);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                    if (apiRequest.ShouldThrowExceptionOnError())
                    {
                        throw new Exception(errorResponse.BuildErrorMessage());
                    }
                    return errorResponse;
                }

                return jsonString;
            }
        }

        public async Task<Either<T, ApilaneError>> GetStatsAggregateAsync<T>(
            StatsAggregateRequest apiRequest,
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                var authorizationToken = await GetAuthTokenAsync(apiRequest);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                    if (apiRequest.ShouldThrowExceptionOnError())
                    {
                        throw new Exception(errorResponse.BuildErrorMessage());
                    }
                    return errorResponse;
                }

                return JsonSerializer.Deserialize<T>(jsonString, JsonDeserializerSettings)!;
            }
        }

        public async Task<Either<string, ApilaneError>> GetStatsDistinctAsync(StatsDistinctRequest apiRequest, CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                var authorizationToken = await GetAuthTokenAsync(apiRequest);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                    if (apiRequest.ShouldThrowExceptionOnError())
                    {
                        throw new Exception(errorResponse.BuildErrorMessage());
                    }
                    return errorResponse;
                }

                return jsonString;
            }
        }

        public async Task<Either<T, ApilaneError>> GetStatsDistinctAsync<T>(StatsDistinctRequest apiRequest, CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                var authorizationToken = await GetAuthTokenAsync(apiRequest);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                    if (apiRequest.ShouldThrowExceptionOnError())
                    {
                        throw new Exception(errorResponse.BuildErrorMessage());
                    }
                    return errorResponse;
                }

                return JsonSerializer.Deserialize<T>(jsonString, JsonDeserializerSettings)!;
            }
        }
    }
}
