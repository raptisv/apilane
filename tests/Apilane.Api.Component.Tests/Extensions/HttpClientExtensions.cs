using Apilane.Common;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests.Extensions
{
    public static class HttpClientExtensions
    {
        public static async Task<HttpResponseMessage> RequestAsync(
            this HttpClient client, 
            HttpMethod method, 
            string path, 
            object? payload = null)
        {
            if (!client.DefaultRequestHeaders.Any(x => x.Key == Globals.ClientIdHeaderName))
            {
                client.DefaultRequestHeaders.Add(Globals.ClientIdHeaderName, Globals.ClientIdHeaderValuePortal);
            }

            return method.Method.ToUpper() switch
            {
                "GET" => await client.GetAsync(path),
                "POST" => await client.PostAsync(path, payload?.ToJsonData()),
                "PUT" => await client.PutAsync(path, payload?.ToJsonData()),
                "DELETE" => await client.DeleteAsync(path),
                _ => throw new Exception($"Invalid method {method.Method}")
            };
        }

        public static T? DeserializeTo<T>(this string data)
        {
            return JsonSerializer.Deserialize<T>(data);
        }

        public static HttpContent ToJsonData(this object data)
        {
            var json = JsonSerializer.Serialize(data);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        public static async Task<T?> RequestAsync<T>(
            this HttpClient client,
            HttpMethod method,
            string path,
            object? payload = null)
        {
            var response = await client.RequestAsync(method, path, payload);

            Assert.NotNull(response);

            var responseText = await (response?.Content.ReadAsStringAsync() ?? throw new Exception("Response was null"));

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Http Error: {response.StatusCode} on {method} {path} {responseText}");
            }

            try
            {
                return responseText.DeserializeTo<T>();
            }
            catch (Exception ex)
            {
                throw new Exception($"Cannot Parse: {responseText}", ex);
            }
        }
    }
}