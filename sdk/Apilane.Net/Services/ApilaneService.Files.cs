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
            FileGetListRequest request,
            JsonSerializerOptions? customJsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
        {
            request.WithTotal(false);

            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.GetUrl(_config.ApplicationApiUrl)))
            {
                await ApplyAuthAsync(httpRequest, request);
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

        public async Task<Either<T, ApilaneError>> GetFileByIdAsync<T>(
            FileGetByIdRequest request,
            JsonSerializerOptions? customJsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.GetUrl(_config.ApplicationApiUrl)))
            {
                await ApplyAuthAsync(httpRequest, request);
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

        /// <summary>
        /// Returns the newly created IDs
        /// </summary>
        public async Task<Either<long?, ApilaneError>> PostFileAsync(FilePostRequest request, byte[] data, CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.GetUrl(_config.ApplicationApiUrl)))
            {
                var form = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(data);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
                form.Add(fileContent, "FileUpload", request.GetFileName());

                httpRequest.Content = form;

                // Request signing is not supported for file uploads (it would require buffering the
                // whole multipart body to hash it). File uploads authenticate with the bearer token.
                if (request.HasSigning(out _, out _))
                {
                    throw new InvalidOperationException("Request signing is not supported for file uploads. Use WithAuthToken for PostFileAsync.");
                }

                if (request.HasAuthToken(out var authToken))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
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

                return JsonSerializer.Deserialize<long>(jsonString, JsonDeserializerSettings);
            }
        }

        public async Task<Either<long[], ApilaneError>> DeleteFileAsync(
            FileDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Delete, request.GetUrl(_config.ApplicationApiUrl)))
            {
                await ApplyAuthAsync(httpRequest, request);
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
