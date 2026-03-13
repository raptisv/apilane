using Apilane.Net;
using Apilane.Net.Abstractions;
using Apilane.Net.Services;
using System.Net.Http;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ApilaneExtensions
    {
        private const string DefaultHttpClientName = "Apilane";

        public static IServiceCollection UseApilane(
            this IServiceCollection services,
            string applicationApiUrl,
            string applicationToken,
            IApilaneAuthTokenProvider? apilaneAuthTokenProvider = null,
            string? serviceKey = null)
        {
            var config = new ApilaneConfiguration()
            {
                ApplicationApiUrl = applicationApiUrl,
                ApplicationToken = applicationToken
            };

            var httpClientName = string.IsNullOrWhiteSpace(serviceKey)
                ? DefaultHttpClientName
                : $"{DefaultHttpClientName}_{serviceKey}";

            services.AddHttpClient(httpClientName);

            if (string.IsNullOrWhiteSpace(serviceKey))
            {
                services.AddSingleton<IApilaneService>((serviceProvider) =>
                {
                    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                    // Optional global auth token provider
                    var authTokenProvider = apilaneAuthTokenProvider ?? serviceProvider.GetService<IApilaneAuthTokenProvider>();

                    return new ApilaneService(
                        httpClientFactory.CreateClient(httpClientName),
                        config,
                        authTokenProvider);
                });
            }
            else
            {
                // Keyed registration
                services.AddKeyedSingleton<IApilaneService>(serviceKey, (serviceProvider, key) =>
                {
                    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                    // Optional global auth token provider
                    var authTokenProvider = apilaneAuthTokenProvider ?? serviceProvider.GetService<IApilaneAuthTokenProvider>();

                    return new ApilaneService(
                        httpClientFactory.CreateClient(httpClientName),
                        config,
                        authTokenProvider);
                });
            }

            return services;
        }
    }
}
