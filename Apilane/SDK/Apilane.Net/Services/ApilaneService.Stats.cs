using Apilane.Net.Abstractions;
using Apilane.Net.Models.Data;
using Apilane.Net.Request;
using Apilane.Net.Utilities;
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
                if (apiRequest.HasAuthToken(out string authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
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
                if (apiRequest.HasAuthToken(out string authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                }

                return JsonSerializer.Deserialize<T>(jsonString, JsonDeserializerSettings)!;
            }
        }

        public async Task<Either<string, ApilaneError>> GetStatsDistinctAsync(StatsDistinctRequest apiRequest, CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                if (apiRequest.HasAuthToken(out string authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                }

                return jsonString;
            }
        }

        public async Task<Either<T, ApilaneError>> GetStatsDistinctAsync<T>(StatsDistinctRequest apiRequest, CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                if (apiRequest.HasAuthToken(out string authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                }

                return JsonSerializer.Deserialize<T>(jsonString, JsonDeserializerSettings)!;
            }
        }
    }
}
