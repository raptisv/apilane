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
        public async Task<Either<DataResponse<T>, ApilaneError>> GetFilesAsync<T>(
            FileGetListRequest apiRequest,
            JsonSerializerOptions? customJsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
        {
            apiRequest.WithTotal(false);

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

                return JsonSerializer.Deserialize<DataResponse<T>>(jsonString, customJsonSerializerOptions ?? JsonDeserializerSettings)!;
            }
        }

        public async Task<Either<T, ApilaneError>> GetFileByIdAsync<T>(
            FileGetByIdRequest apiRequest,
            JsonSerializerOptions? customJsonSerializerOptions = null,
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

                return JsonSerializer.Deserialize<T>(jsonString, customJsonSerializerOptions ?? JsonDeserializerSettings)!;
            }
        }

        /// <summary>
        /// Returns the newly created IDs
        /// </summary>
        public async Task<Either<long?, ApilaneError>> PostFileAsync(FilePostRequest apiRequest, byte[] data, CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiRequest.GetUrl(_config.ApplicationApiUrl)))
            {
                var form = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(data);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
                form.Add(fileContent, "FileUpload", apiRequest.GetFileName());

                httpRequest.Content = form;
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

                return JsonSerializer.Deserialize<long>(jsonString, JsonDeserializerSettings);
            }
        }

        public async Task<Either<long[], ApilaneError>> DeleteFileAsync(
            FileDeleteRequest apiRequest,
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Delete, apiRequest.GetUrl(_config.ApplicationApiUrl)))
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

                return JsonSerializer.Deserialize<long[]>(jsonString, JsonDeserializerSettings)!;
            }
        }
    }
}
