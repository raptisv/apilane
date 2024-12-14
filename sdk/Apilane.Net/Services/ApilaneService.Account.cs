using Apilane.Net.Abstractions;
using Apilane.Net.Extensions;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Request;
using Apilane.Net.Utilities;
using System;
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
            AccountLoginRequest request,
            CancellationToken cancellationToken = default) where T : IApiUser
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.GetUrl(_config.ApplicationApiUrl)))
            {
                httpRequest.Content = new StringContent(JsonSerializer.Serialize(request.LoginItem), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                    if (request.ShouldThrowExceptionOnError())
                    {
                        throw new Exception(errorResponse.BuildErrorMessage());
                    }
                    return errorResponse;
                }

                return JsonSerializer.Deserialize<AccountLoginResponse<T>>(jsonString, JsonDeserializerSettings)!;
            }
        }

        public async Task<Either<string, ApilaneError>> AccountRenewAuthTokenAsync(
            AccountRenewAuthTokenRequest request,
            CancellationToken cancellationToken = default) 
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.GetUrl(_config.ApplicationApiUrl)))
            {
                var authorizationToken = await GetAuthTokenAsync(request);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                    if (request.ShouldThrowExceptionOnError())
                    {
                        throw new Exception(errorResponse.BuildErrorMessage());
                    }
                    return errorResponse;
                }

                return jsonString;
            }
        }

        /// <summary>
        ///  
        /// </summary>
        /// <returns></returns>
        public async Task<Either<AccountUserDataResponse<T>, ApilaneError>> GetAccountUserDataAsync<T>(
            AccountUserDataRequest request,
            CancellationToken cancellationToken = default) where T : IApiUser
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.GetUrl(_config.ApplicationApiUrl)))
            {
                var authorizationToken = await GetAuthTokenAsync(request);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                    if (request.ShouldThrowExceptionOnError())
                    {
                        throw new Exception(errorResponse.BuildErrorMessage());
                    }
                    return errorResponse;
                }

                return JsonSerializer.Deserialize<AccountUserDataResponse<T>>(jsonString, JsonDeserializerSettings)!;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<Either<long, ApilaneError>> AccountRegisterAsync(
            AccountRegisterRequest request,
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.GetUrl(_config.ApplicationApiUrl)))
            {
                httpRequest.Content = new StringContent(JsonSerializer.Serialize((object)request.RegisterItem), Encoding.UTF8, "application/json");
                var authorizationToken = await GetAuthTokenAsync(request);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                    if (request.ShouldThrowExceptionOnError())
                    {
                        throw new Exception(errorResponse.BuildErrorMessage());
                    }
                    return errorResponse;
                }

                var userId = long.TryParse(jsonString, out long result) ? result : 0;

                return userId;
            }
        }

        public async Task<Either<T, ApilaneError>> AccountUpdateAsync<T>(
            AccountUpdateRequest request,
            object updateItem,
            CancellationToken cancellationToken = default) where T : IApiUser
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Put, request.GetUrl(_config.ApplicationApiUrl)))
            {
                var authorizationToken = await GetAuthTokenAsync(request);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                httpRequest.Content = new StringContent(JsonSerializer.Serialize(updateItem), Encoding.UTF8, "application/json");
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                    if (request.ShouldThrowExceptionOnError())
                    {
                        throw new Exception(errorResponse.BuildErrorMessage());
                    }
                    return errorResponse;
                }

                return JsonSerializer.Deserialize<T>(jsonString, JsonDeserializerSettings)!;
            }
        }

        public async Task<Either<int, ApilaneError>> AccountLogoutAsync(
            AccountLogoutRequest request,
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.GetUrl(_config.ApplicationApiUrl)))
            {
                var authorizationToken = await GetAuthTokenAsync(request);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                    if (request.ShouldThrowExceptionOnError())
                    {
                        throw new Exception(errorResponse.BuildErrorMessage());
                    }
                    return errorResponse;
                }

                var loggedOutCount = int.TryParse(jsonString, out int result) ? result : 0;

                return loggedOutCount;
            }
        }
    }
}
