using Apilane.Net;
using Apilane.Net.Abstractions;
using Apilane.Net.Services;
using System.Net.Http;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ApilaneExtensions
    {
        public static IServiceCollection UseApilane(
            this IServiceCollection services,
            string applicationApiUrl,
            string applicationToken,
            HttpClient? httpClient = null,
            IApilaneAuthTokenProvider? apilaneAuthTokenProvider = null)
        {
            services.AddSingleton<IApilaneService>((serviceProvider) =>
            {
                // Optional global auth token provider
                var authTokenProvider = apilaneAuthTokenProvider ?? serviceProvider.GetService<IApilaneAuthTokenProvider>();

                return new ApilaneService(
                    httpClient ?? new HttpClient(),
                    new ApilaneConfiguration()
                    {
                        ApplicationApiUrl = applicationApiUrl,
                        ApplicationToken = applicationToken
                    },
                    authTokenProvider);
            });

            return services;
        }
    }
}
