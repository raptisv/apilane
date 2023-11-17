using Apilane.Net.Abstractions;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Request;
using Apilane.Net.Utilities;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Net.Services
{
    public sealed partial class ApilaneService : IApilaneService
    {
        public async Task<Either<AccountLoginResponse<T>, ApilaneError>> AccountLoginAsync<T>(
            LoginItem loginItem,
            CancellationToken cancellationToken = default) where T : IApiUser
        {
            var apiRequest = AccountLoginRequest.New();
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                httpRequest.Content = new StringContent(JsonSerializer.Serialize(loginItem), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                }

                return JsonSerializer.Deserialize<AccountLoginResponse<T>>(jsonString, JsonDeserializerSettings)!;
            }
        }

        public async Task<Either<string, ApilaneError>> AccountRenewAuthTokenAsync(
            string authorizationToken,
            CancellationToken cancellationToken = default) 
        {
            var apiRequest = AccountRenewAuthTokenRequest.New();
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                }

                return jsonString;
            }
        }

        /// <summary>
        ///  
        /// </summary>
        /// <returns></returns>
        public async Task<Either<AccountUserDataResponse<T>, ApilaneError>> GetAccountUserDataAsync<T>(
            string authToken,
            CancellationToken cancellationToken = default) where T : IApiUser
        {
            var apiRequest = AccountUserDataRequest.New().WithAuthToken(authToken);

            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                if (apiRequest.HasAuthToken(out string authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                }

                return JsonSerializer.Deserialize<AccountUserDataResponse<T>>(jsonString, JsonDeserializerSettings)!;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<Either<long, ApilaneError>> AccountRegisterAsync(
            IRegisterItem registerItem,
            CancellationToken cancellationToken = default)
        {
            var apiRequest = AccountRegisterRequest.New();
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                httpRequest.Content = new StringContent(JsonSerializer.Serialize((object)registerItem), Encoding.UTF8, "application/json");
                if (apiRequest.HasAuthToken(out string authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                }

                var userId = long.TryParse(jsonString, out long result) ? result : 0;

                return userId;
            }
        }

        public async Task<Either<T, ApilaneError>> AccountUpdateAsync<T>(
            string authToken,
            object updateItem,
            CancellationToken cancellationToken = default) where T : IApiUser
        {
            var apiRequest = AccountUpdateRequest.New().WithAuthToken(authToken);
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Put, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                if (apiRequest.HasAuthToken(out string authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                httpRequest.Content = new StringContent(JsonSerializer.Serialize(updateItem), Encoding.UTF8, "application/json");
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                }

                return JsonSerializer.Deserialize<T>(jsonString, JsonDeserializerSettings)!;
            }
        }

        public async Task<Either<int, ApilaneError>> AccountLogoutAsync(
            string authToken,
            bool logOutFromEverywhere = false,
            CancellationToken cancellationToken = default)
        {
            var apiRequest = AccountLogoutRequest.New(logOutFromEverywhere).WithAuthToken(authToken);
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                if (apiRequest.HasAuthToken(out string authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                }

                var loggedOutCount = int.TryParse(jsonString, out int result) ? result : 0;

                return loggedOutCount;
            }
        }
    }
}
