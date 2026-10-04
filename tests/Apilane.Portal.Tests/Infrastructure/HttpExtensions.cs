using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests.Infrastructure
{
    public static class HttpExtensions
    {
        // Property names are matched case-sensitively and unknown ones are refused, so reading a
        // contract class fails on a camelCase body and on a property the contract does not have.
        private static readonly JsonSerializerOptions _strictOptions = new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        // Property names are written as declared (PascalCase), not in the camelCase HttpClient defaults to.
        private static readonly JsonSerializerOptions _writeOptions = new JsonSerializerOptions();

        public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response)
        {
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

            var json = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<T>(json, _strictOptions)
                ?? throw new InvalidOperationException("The response body was JSON null.");
        }

        public static HttpContent ToJsonContent(this object value)
        {
            return JsonContent.Create(value, options: _writeOptions);
        }

        /// <summary>
        /// The cookies a response sets, as a Cookie request header value.
        /// </summary>
        public static string GetSetCookieHeader(this HttpResponseMessage response)
        {
            return response.Headers.TryGetValues("Set-Cookie", out var values)
                ? string.Join("; ", values.Select(x => x.Split(';')[0]))
                : string.Empty;
        }

        /// <summary>
        /// Whether a response sets a cookie that outlives the browser session (it has an expiry date).
        /// </summary>
        public static bool SetsPersistentCookie(this HttpResponseMessage response)
        {
            return response.Headers.TryGetValues("Set-Cookie", out var values)
                && values.Any(x => x.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Whether a response tells the browser to drop the login cookie (empty value, expiry in the past).
        /// </summary>
        public static bool RemovesLoginCookie(this HttpResponseMessage response)
        {
            return response.Headers.TryGetValues("Set-Cookie", out var values)
                && values.Any(x => x.StartsWith("Apilane.Portal.Identity=;", StringComparison.Ordinal)
                    && x.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        }

        public static Task<HttpResponseMessage> GetWithCookieAsync(this HttpClient client, string url, string cookie)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Cookie", cookie);

            return client.SendAsync(request);
        }
    }
}
