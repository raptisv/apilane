using Apilane.Net.Abstractions;
using Apilane.Net.Extensions;
using Apilane.Net.Models;
using Apilane.Net.Models.Data;
using Apilane.Net.Request;
using Apilane.Net.Utilities;
using System;
using System.Collections.Generic;
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
        public async Task<Either<ApplicationSchemaDto, ApilaneError>> GetApplicationSchemaAsync(
            DataGetSchemaRequest request,
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

                return JsonSerializer.Deserialize<ApplicationSchemaDto>(jsonString, JsonDeserializerSettings)!;
            }
        }

        public async Task<Either<T, ApilaneError>> GetDataByIdAsync<T>(
            DataGetByIdRequest request,
            JsonSerializerOptions? customJsonSerializerOptions = null,
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

                return JsonSerializer.Deserialize<T>(jsonString, customJsonSerializerOptions ?? JsonDeserializerSettings)!;
            }
        }

        public async Task<Either<DataResponse<T>, ApilaneError>> GetDataAsync<T>(
            DataGetListRequest request,
            JsonSerializerOptions? customJsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
        {
            request.WithTotal(false);

            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.GetUrl(_config.ApplicationApiUrl)))
            {
                var authorizationToken = await GetAuthTokenAsync(request);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
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

                return JsonSerializer.Deserialize<DataResponse<T>>(jsonString, customJsonSerializerOptions ?? JsonDeserializerSettings)!;
            }
        }

        public async Task<Either<List<T>, ApilaneError>> GetAllDataAsync<T>(
            DataGetAllRequest request,
            JsonSerializerOptions? customJsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
        {
            int pageIndex = 1;
            int pageSize = 1000;

            var getListRequest = DataGetListRequest.New(request.Entity)
                .WithPageIndex(pageIndex)
                .WithPageSize(pageSize)
                .WithTotal(false)
                .OnErrorThrowException(request.ShouldThrowExceptionOnError());

            if (request.AuthToken is not null)
            {
                getListRequest = getListRequest.WithAuthToken(request.AuthToken);
            }

            if (request.Filter is not null)
            {
                getListRequest = getListRequest.WithFilter(request.Filter);
            }

            if (request.Sort is not null)
            {
                getListRequest = getListRequest.WithSort(request.Sort);
            }

            if (request.Properties is not null)
            {
                getListRequest = getListRequest.WithProperties(request.Properties.ToArray());
            }

            var result = new List<T>();

            var nextPage = await GetDataAsync<T>(getListRequest, customJsonSerializerOptions, cancellationToken);

            if (nextPage.HasError(out var error))
            {
                return error;
            }

            result.AddRange(nextPage.Value.Data);

            // While the data fetched equals the page size, meaning there must be also other elements to fetch.
            while (nextPage.Value.Data.Count >= pageSize)
            {
                pageIndex++;

                // Set the next page index
                getListRequest = getListRequest.WithPageIndex(pageIndex);

                nextPage = await GetDataAsync<T>(getListRequest, customJsonSerializerOptions, cancellationToken);

                if (nextPage.HasError(out var pageError))
                {
                    return pageError;
                }

                result.AddRange(nextPage.Value.Data);
            }

            return result;
        }

        public async Task<Either<DataTotalResponse<T>, ApilaneError>> GetDataTotalAsync<T>(
            DataGetListRequest request,
            JsonSerializerOptions? customJsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
        {
            request.WithTotal(true);

            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.GetUrl(_config.ApplicationApiUrl)))
            {
                var authorizationToken = await GetAuthTokenAsync(request);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
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

                return JsonSerializer.Deserialize<DataTotalResponse<T>>(jsonString, customJsonSerializerOptions ?? JsonDeserializerSettings)!;
            }
        }

        /// <summary>
        /// Returns the newly created IDs + update affected rows + deleted records
        /// </summary>
        public async Task<Either<OutTransactionData, ApilaneError>> TransactionDataAsync(
            DataTransactionRequest request,
            InTransactionData data,
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.GetUrl(_config.ApplicationApiUrl)))
            {
                httpRequest.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
                var authorizationToken = await GetAuthTokenAsync(request);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
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

                return JsonSerializer.Deserialize<OutTransactionData>(jsonString, JsonDeserializerSettings)!;
            }
        }

        /// <summary>
        /// Returns the newly created IDs
        /// </summary>
        public async Task<Either<long[], ApilaneError>> PostDataAsync(
            DataPostRequest request,
            object data,
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.GetUrl(_config.ApplicationApiUrl)))
            {
                httpRequest.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
                var authorizationToken = await GetAuthTokenAsync(request);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
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

                return JsonSerializer.Deserialize<long[]>(jsonString, JsonDeserializerSettings)!;
            }
        }

        /// <summary>
        /// Returns the rows affected fro the update action
        /// </summary>
        public async Task<Either<int, ApilaneError>> PutDataAsync(
            DataPutRequest request,
            object data,
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Put, request.GetUrl(_config.ApplicationApiUrl)))
            {
                httpRequest.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

                var authorizationToken = await GetAuthTokenAsync(request);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
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

                return int.Parse(jsonString);
            }
        }

        public async Task<Either<long[], ApilaneError>> DeleteDataAsync(
            DataDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Delete, request.GetUrl(_config.ApplicationApiUrl)))
            {
                var authorizationToken = await GetAuthTokenAsync(request);
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
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

                return JsonSerializer.Deserialize<long[]>(jsonString, JsonDeserializerSettings)!;
            }
        }
    }
}
