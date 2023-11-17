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
            HttpClient? httpClient = null)
        {
            services.AddSingleton<IApilaneService>((s) => new ApilaneService(httpClient ?? new HttpClient(), new ApilaneConfiguration()
            {
                ApplicationApiUrl = applicationApiUrl,
                ApplicationToken = applicationToken
            }));

            return services;
        }
    }
}
