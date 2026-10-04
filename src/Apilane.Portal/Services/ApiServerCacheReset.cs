using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ApiServerCacheReset : IApiServerCacheReset
    {
        public const string WarningHeaderName = "Warning";
        public const string WarningText = "Saved, but the API server could not be refreshed.";

        private readonly IApiServerClient _apiServerClient;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<ApiServerCacheReset> _logger;

        public ApiServerCacheReset(
            IApiServerClient apiServerClient,
            IHttpContextAccessor httpContextAccessor,
            ILogger<ApiServerCacheReset> logger)
        {
            _apiServerClient = apiServerClient;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task ResetAfterWriteAsync(DBWS_Application application)
        {
            try
            {
                await _apiServerClient.ClearCacheAsync(application);
            }
            catch (PortalException exception)
            {
                // The change is saved; failing the request now would make the caller repeat it.
                _logger.LogWarning(exception, "Cache reset after a write failed | Application {Token}", application.Token);

                var response = _httpContextAccessor.HttpContext?.Response;

                if (response is not null)
                {
                    response.Headers[WarningHeaderName] = WarningText;
                }
            }
        }
    }
}
