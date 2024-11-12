using Apilane.Api.Abstractions;
using Apilane.Common.Models;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;
using System.Threading.Tasks;

namespace Apilane.Api.Grains
{
    public interface IApplicationGrain : IGrainWithGuidKey
    {
        [AlwaysInterleave]
        Task<DBWS_Application> GetAsync();
        Task ClearCacheAsync();
    }

    public class ApplicationGrain : Grain, IApplicationGrain
    {
        private readonly ILogger<ApplicationGrain> _logger;
        private readonly IPortalInfoService _portalInfoService;

        private DBWS_Application? _application = null;

        public ApplicationGrain(
            ILogger<ApplicationGrain> logger,
            IPortalInfoService portalInfoService)
        {
            _logger = logger;
            _portalInfoService = portalInfoService;
        }

        public async Task<DBWS_Application> GetAsync()
        {
            var appToken = this.GetPrimaryKey(out _).ToString();
            _application ??= await _portalInfoService.GetApplicationAsync(appToken);

            return _application;
        }

        public Task ClearCacheAsync()
        {
            _application = null;
            return Task.CompletedTask;
        }
    }
}
