using Apilane.Common;
using Apilane.Common.Helpers;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ApiHttpService : IApiHttpService
    {
        private readonly IHttpClientFactory _clientFactory;

        public ApiHttpService(
            IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        private async Task<Either<string, HttpStatusCode>> GetAsync(
            string url,
            string appToken,
            string portalUserAuthToken)
        {
            using (var client = _clientFactory.CreateClient("Api"))
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                request.Headers.Add(Globals.ClientIdHeaderName, Globals.ClientIdHeaderValuePortal);
                request.Headers.Add(Globals.ApplicationTokenHeaderName, appToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", portalUserAuthToken);

                var httpResponse = await client.SendAsync(request);

                var strReponse = await httpResponse.Content.ReadAsStringAsync();

                if (!httpResponse.IsSuccessStatusCode)
                {
                    Log.Logger.Error($"ApiHttpService | Error GetAsync | StatusCode {httpResponse.StatusCode} | ReasonPhrase {httpResponse.ReasonPhrase}");
                    throw new Exception(ExtractDebugMessage(strReponse));
                }

                return strReponse;
            }
        }

        private async Task<Either<T, HttpStatusCode>> GetAsync<T>(
            string url,
            string appToken,
            string portalUserAuthToken)
        {
            var result = await GetAsync(url, appToken, portalUserAuthToken);

            if (result.IsError(out var error))
            {
                return error;
            }

            return JsonSerializer.Deserialize<T>(result.Value)
                ?? throw new Exception("Result cannot be null");
        }

        public async Task<Either<DataTotalResponse, HttpStatusCode>> GetDataPageAsync(
            string serverUrl,
            string appToken,
            string entity,
            int pageIndex,
            int pageSize,
            string portalUserAuthToken)
        {
            var url = $"{serverUrl.Trim('/')}/api/data/get?entity={entity}&pageIndex={pageIndex}&pageSize={pageSize}&getTotal=true";
            var result = await GetAsync<DataTotalResponse>(url, appToken, portalUserAuthToken);

            if (result.IsError(out var error))
            {
                return error;
            }

            return result.Value;
        }

        public async Task<Either<List<long>, HttpStatusCode>> ImportDataAsync(
            string serverUrl,
            string appToken,
            string entity,
            List<Dictionary<string, object?>> postData,
            string portalUserAuthToken)
        {
            using (var client = _clientFactory.CreateClient("Api"))
            {
                var url = $"{serverUrl.Trim('/')}/api/Application/ImportData?Entity={entity}";
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(postData), Encoding.UTF8, "application/json")
                };

                request.Headers.Add(Globals.ClientIdHeaderName, Globals.ClientIdHeaderValuePortal);
                request.Headers.Add(Globals.ApplicationTokenHeaderName, appToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", portalUserAuthToken);

                var httpResponse = await client.SendAsync(request);

                var strReponse = await httpResponse.Content.ReadAsStringAsync();

                if (!httpResponse.IsSuccessStatusCode)
                {
                    Log.Logger.Error($"ApiHttpService | Error PostAsync | StatusCode {httpResponse.StatusCode} | ReasonPhrase {httpResponse.ReasonPhrase}");
                    throw new Exception(ExtractDebugMessage(strReponse));
                }

                return JsonSerializer.Deserialize<List<long>>(strReponse)
                    ?? throw new Exception("Result cannot be null");
            }
        }

        public async Task<Either<string, HttpStatusCode>> PostAsync(
            string url,
            string appToken,
            string portalUserAuthToken,
            object postData,
            IDictionary<string, string>? headers = null)
        {
            using (var client = _clientFactory.CreateClient("Api"))
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(postData), Encoding.UTF8, "application/json")
                };

                request.Headers.Add(Globals.ClientIdHeaderName, Globals.ClientIdHeaderValuePortal);
                request.Headers.Add(Globals.ApplicationTokenHeaderName, appToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", portalUserAuthToken);

                if (headers is not null)
                {
                    foreach (var header in headers)
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }
                }

                var httpResponse = await client.SendAsync(request);

                var strReponse = await httpResponse.Content.ReadAsStringAsync();

                if (!httpResponse.IsSuccessStatusCode)
                {
                    Log.Logger.Error($"ApiHttpService | Error PostAsync | StatusCode {httpResponse.StatusCode} | ReasonPhrase {httpResponse.ReasonPhrase}");
                    throw new Exception(ExtractDebugMessage(strReponse));
                }

                return strReponse;
            }
        }

        private static string ExtractDebugMessage(string apiStrResponse)
        {
            try
            {
                var jObj = JsonObject.Parse(apiStrResponse)!.AsObject();

                if (jObj.ContainsKey("Message"))
                {
                    var error = jObj["Message"]!.ToString();

                    return string.IsNullOrWhiteSpace(error)
                        ? apiStrResponse
                        : error;
                }

                if (jObj.ContainsKey("Debug"))
                {
                    var error = jObj["Debug"]!.ToString();

                    return string.IsNullOrWhiteSpace(error)
                        ? apiStrResponse
                        : error;
                }
            }
            catch { }

            return apiStrResponse;
        }
    }
}
