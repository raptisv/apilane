using Apilane.Net.Abstractions;
using Apilane.Net.Extensions;
using Apilane.Net.Models.Data;
using Apilane.Net.Request;
using Apilane.Net.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Net.Services
{
    public sealed partial class ApilaneService : IApilaneService
    {
        public async Task<Either<T, ApilaneError>> GetCustomEndpointAsync<T>(
            CustomEndpointRequest customendpoint,
            CancellationToken cancellationToken = default)
        {
            var response = await GetCustomEndpointAsync(customendpoint, cancellationToken);

            Either<T, ApilaneError> result = null!;

            response.Match(
                success =>
                {
                    result = Load<T>(success);
                },
                error =>
                {
                    result = error;
                }
            );

            return result;
        }

        public async Task<Either<(T1 Data1, T2 Data2), ApilaneError>> GetCustomEndpointAsync<T1, T2>(
            CustomEndpointRequest customendpoint,
            CancellationToken cancellationToken = default)
        {
            var response = await GetCustomEndpointAsync(customendpoint, cancellationToken);

            Either<(T1 Data1, T2 Data2), ApilaneError> result = null!;

            response.Match(
                success =>
                {
                    result = Load<T1, T2>(success);
                },
                error =>
                {
                    result = error;
                }
            );

            return result;
        }

        public async Task<Either<(T1 Data1, T2 Data2, T3 Data3), ApilaneError>> GetCustomEndpointAsync<T1, T2, T3>(
            CustomEndpointRequest customendpoint,
            CancellationToken cancellationToken = default)
        {
            var response = await GetCustomEndpointAsync(customendpoint, cancellationToken);

            Either<(T1 Data1, T2 Data2, T3 Data3), ApilaneError> result = null!;

            response.Match(
                success =>
                {
                    result = Load<T1, T2, T3>(success);
                },
                error =>
                {
                    result = error;
                }
            );

            return result;
        }

        public async Task<Either<(T1 Data1, T2 Data2, T3 Data3, T4 Data4), ApilaneError>> GetCustomEndpointAsync<T1, T2, T3, T4>(
            CustomEndpointRequest customendpoint,
            CancellationToken cancellationToken = default)
        {
            var response = await GetCustomEndpointAsync(customendpoint, cancellationToken);

            Either<(T1 Data1, T2 Data2, T3 Data3, T4 Data4), ApilaneError> result = null!;

            response.Match(
                success =>
                {
                    result = Load<T1, T2, T3, T4>(success);
                },
                error =>
                {
                    result = error;
                }
            );

            return result;
        }

        public async Task<Either<(T1 Data1, T2 Data2, T3 Data3, T4 Data4, T5 Data5), ApilaneError>> GetCustomEndpointAsync<T1, T2, T3, T4, T5>(
            CustomEndpointRequest customendpoint,
            CancellationToken cancellationToken = default)
        {
            var response = await GetCustomEndpointAsync(customendpoint, cancellationToken);

            Either<(T1 Data1, T2 Data2, T3 Data3, T4 Data4, T5 Data5), ApilaneError> result = null!;

            response.Match(
                success =>
                {
                    result = Load<T1, T2, T3, T4, T5>(success);
                },
                error =>
                {
                    result = error;
                }
            );

            return result;
        }

        public async Task<Either<string, ApilaneError>> GetCustomEndpointAsync(
            CustomEndpointRequest apiRequest,
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
                response.Dispose();

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

        private T Load<T>(string jsonResult)
        {
            if (!string.IsNullOrWhiteSpace(jsonResult))
            {
                var jsonArray = JsonObject.Parse(jsonResult)!.AsArray();

                object temp = JsonSerializer.Deserialize(jsonArray[0]!.ToString(), GetListTypeForGivenGeneric<T>(), JsonDeserializerSettings)!;

                return
                    IsTypeOfList(typeof(T)) ? (T)temp : ((List<T>)temp).FirstOrDefault();
            }

            return default!;
        }

        private (T1 Data1, T2 Data2) Load<T1, T2>(string jsonResult)
        {
            if (!string.IsNullOrWhiteSpace(jsonResult))
            {
                var jsonArray = JsonObject.Parse(jsonResult)!.AsArray();

                (object, object) temp = (
                        JsonSerializer.Deserialize(jsonArray[0]!.ToString(), GetListTypeForGivenGeneric<T1>(), JsonDeserializerSettings)!,
                        JsonSerializer.Deserialize(jsonArray[1]!.ToString(), GetListTypeForGivenGeneric<T2>(), JsonDeserializerSettings)!);

                return (
                    IsTypeOfList(typeof(T1)) ? (T1)temp.Item1 : ((List<T1>)temp.Item1).FirstOrDefault()!,
                    IsTypeOfList(typeof(T2)) ? (T2)temp.Item2 : ((List<T2>)temp.Item2).FirstOrDefault())!;
            }

            return (default!, default!);
        }

        private (T1 Data1, T2 Data2, T3 Data3) Load<T1, T2, T3>(string jsonResult)
        {
            if (!string.IsNullOrWhiteSpace(jsonResult))
            {
                var jsonArray = JsonObject.Parse(jsonResult)!.AsArray();

                (object, object, object) temp = (
                        JsonSerializer.Deserialize(jsonArray[0]!.ToString(), GetListTypeForGivenGeneric<T1>(), JsonDeserializerSettings)!,
                        JsonSerializer.Deserialize(jsonArray[1]!.ToString(), GetListTypeForGivenGeneric<T2>(), JsonDeserializerSettings)!,
                        JsonSerializer.Deserialize(jsonArray[2]!.ToString(), GetListTypeForGivenGeneric<T3>(), JsonDeserializerSettings)!);

                return (
                    IsTypeOfList(typeof(T1)) ? (T1)temp.Item1 : ((List<T1>)temp.Item1).FirstOrDefault(),
                    IsTypeOfList(typeof(T2)) ? (T2)temp.Item2 : ((List<T2>)temp.Item2).FirstOrDefault(),
                    IsTypeOfList(typeof(T3)) ? (T3)temp.Item3 : ((List<T3>)temp.Item3).FirstOrDefault());
            }

            return (default!, default!, default!);
        }

        private (T1 Data1, T2 Data2, T3 Data3, T4 Data4) Load<T1, T2, T3, T4>(string jsonResult)
        {
            if (!string.IsNullOrWhiteSpace(jsonResult))
            {
                var jsonArray = JsonObject.Parse(jsonResult)!.AsArray();

                (object, object, object, object) temp = (
                        JsonSerializer.Deserialize(jsonArray[0]!.ToString(), GetListTypeForGivenGeneric<T1>(), JsonDeserializerSettings)!,
                        JsonSerializer.Deserialize(jsonArray[1]!.ToString(), GetListTypeForGivenGeneric<T2>(), JsonDeserializerSettings)!,
                        JsonSerializer.Deserialize(jsonArray[2]!.ToString(), GetListTypeForGivenGeneric<T3>(), JsonDeserializerSettings)!,
                        JsonSerializer.Deserialize(jsonArray[3]!.ToString(), GetListTypeForGivenGeneric<T4>(), JsonDeserializerSettings)!);

                return (
                    IsTypeOfList(typeof(T1)) ? (T1)temp.Item1 : ((List<T1>)temp.Item1).FirstOrDefault(),
                    IsTypeOfList(typeof(T2)) ? (T2)temp.Item2 : ((List<T2>)temp.Item2).FirstOrDefault(),
                    IsTypeOfList(typeof(T3)) ? (T3)temp.Item3 : ((List<T3>)temp.Item3).FirstOrDefault(),
                    IsTypeOfList(typeof(T4)) ? (T4)temp.Item4 : ((List<T4>)temp.Item4).FirstOrDefault());
            }

            return (default!, default!, default!, default!);
        }

        private (T1 Data1, T2 Data2, T3 Data3, T4 Data4, T5 Data5) Load<T1, T2, T3, T4, T5>(string jsonResult)
        {
            if (!string.IsNullOrWhiteSpace(jsonResult))
            {
                var jsonArray = JsonObject.Parse(jsonResult)!.AsArray();

                (object, object, object, object, object) temp = (
                        JsonSerializer.Deserialize(jsonArray[0]!.ToString(), GetListTypeForGivenGeneric<T1>(), JsonDeserializerSettings)!,
                        JsonSerializer.Deserialize(jsonArray[1]!.ToString(), GetListTypeForGivenGeneric<T2>(), JsonDeserializerSettings)!,
                        JsonSerializer.Deserialize(jsonArray[2]!.ToString(), GetListTypeForGivenGeneric<T3>(), JsonDeserializerSettings)!,
                        JsonSerializer.Deserialize(jsonArray[3]!.ToString(), GetListTypeForGivenGeneric<T4>(), JsonDeserializerSettings)!,
                        JsonSerializer.Deserialize(jsonArray[4]!.ToString(), GetListTypeForGivenGeneric<T5>(), JsonDeserializerSettings)!);

                return (
                    IsTypeOfList(typeof(T1)) ? (T1)temp.Item1 : ((List<T1>)temp.Item1).FirstOrDefault(),
                    IsTypeOfList(typeof(T2)) ? (T2)temp.Item2 : ((List<T2>)temp.Item2).FirstOrDefault(),
                    IsTypeOfList(typeof(T3)) ? (T3)temp.Item3 : ((List<T3>)temp.Item3).FirstOrDefault(),
                    IsTypeOfList(typeof(T4)) ? (T4)temp.Item4 : ((List<T4>)temp.Item4).FirstOrDefault(),
                    IsTypeOfList(typeof(T5)) ? (T5)temp.Item5 : ((List<T5>)temp.Item5).FirstOrDefault());
            }

            return (default!, default!, default!, default!, default!);
        }

        private Type GetListTypeForGivenGeneric<T>()
        {
            var t = typeof(T);

            if (!IsTypeOfList(t))
            {
                return new List<T>().GetType();
            }

            return t;
        }

        private bool IsTypeOfList(Type t)
        {
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);
        }
    }
}
