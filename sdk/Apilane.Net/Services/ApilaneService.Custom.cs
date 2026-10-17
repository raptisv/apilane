using Apilane.Net.Abstractions;
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
            CustomEndpointRequest request,
            CancellationToken cancellationToken = default)
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.GetUrl(_config.ApplicationApiUrl)))
            {
                await ApplyAuthAsync(httpRequest, request);
                var response = await SendAsync(httpRequest, cancellationToken);
                var jsonString = await response.Content.ReadAsStringAsync();
                response.Dispose();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                    if (request.ShouldThrowExceptionOnError())
                    {
                        throw new ApilaneErrorException(errorResponse, (int)response.StatusCode);
                    }
                    return errorResponse;
                }

                return jsonString;
            }
        }

        private T Load<T>(string jsonResult)
        {
            var resultSets = ParseResultSets(jsonResult);

            return LoadResultSet<T>(resultSets, 0);
        }

        private (T1 Data1, T2 Data2) Load<T1, T2>(string jsonResult)
        {
            var resultSets = ParseResultSets(jsonResult);

            return (
                LoadResultSet<T1>(resultSets, 0),
                LoadResultSet<T2>(resultSets, 1));
        }

        private (T1 Data1, T2 Data2, T3 Data3) Load<T1, T2, T3>(string jsonResult)
        {
            var resultSets = ParseResultSets(jsonResult);

            return (
                LoadResultSet<T1>(resultSets, 0),
                LoadResultSet<T2>(resultSets, 1),
                LoadResultSet<T3>(resultSets, 2));
        }

        private (T1 Data1, T2 Data2, T3 Data3, T4 Data4) Load<T1, T2, T3, T4>(string jsonResult)
        {
            var resultSets = ParseResultSets(jsonResult);

            return (
                LoadResultSet<T1>(resultSets, 0),
                LoadResultSet<T2>(resultSets, 1),
                LoadResultSet<T3>(resultSets, 2),
                LoadResultSet<T4>(resultSets, 3));
        }

        private (T1 Data1, T2 Data2, T3 Data3, T4 Data4, T5 Data5) Load<T1, T2, T3, T4, T5>(string jsonResult)
        {
            var resultSets = ParseResultSets(jsonResult);

            return (
                LoadResultSet<T1>(resultSets, 0),
                LoadResultSet<T2>(resultSets, 1),
                LoadResultSet<T3>(resultSets, 2),
                LoadResultSet<T4>(resultSets, 3),
                LoadResultSet<T5>(resultSets, 4));
        }

        private static JsonArray ParseResultSets(string jsonResult)
        {
            if (string.IsNullOrWhiteSpace(jsonResult))
            {
                return new JsonArray();
            }

            return JsonNode.Parse(jsonResult)?.AsArray() ?? new JsonArray();
        }

        /// <summary>
        /// Reads the result set at the index: a list type gets all its rows, any other type its first row (or its default).
        /// An endpoint can answer fewer result sets than asked for (an UPDATE answers none): a missing one reads as empty.
        /// </summary>
        private T LoadResultSet<T>(JsonArray resultSets, int index)
        {
            var json = index < resultSets.Count && resultSets[index] is JsonNode resultSet
                ? resultSet.ToJsonString()
                : "[]";

            var rows = JsonSerializer.Deserialize(json, GetListTypeForGivenGeneric<T>(), JsonDeserializerSettings)
                ?? throw new JsonException($"Result set {index} of the custom endpoint is not a list of rows");

            return IsTypeOfList(typeof(T)) ? (T)rows : ((List<T>)rows).FirstOrDefault();
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
