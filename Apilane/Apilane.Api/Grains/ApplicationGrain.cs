using Apilane.Api.Abstractions;
using Apilane.Common.Models;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Utilities;
using System;
using System.Threading.Tasks;

namespace Apilane.Api.Grains
{
    public interface IApplicationGrain : IGrainWithGuidKey
    {
        Task<DBWS_Application> GetAsync();
        Task<DBWS_Application> SubscribeAndGetAsync(IApplicationService observer);
        Task ResetStateAsync();
    }

    public class ApplicationGrain : Grain, IApplicationGrain
    {
        private readonly ILogger<ApplicationGrain> _logger;
        private readonly IPortalInfoService _portalInfoService;

        private readonly ObserverManager<IApplicationService> _subsManager;

        private DBWS_Application? _application = null;

        public ApplicationGrain(
            ILogger<ApplicationGrain> logger,
            IPortalInfoService portalInfoService)
        {
            _logger = logger;
            _portalInfoService = portalInfoService;

            _subsManager = new ObserverManager<IApplicationService>(TimeSpan.FromHours(1), logger);

            this.RegisterGrainTimer(OnApplicationExpirationAsync,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
        }

        public async Task OnApplicationExpirationAsync()
        {
            try
            {
                // Attempt reload app from portal on timer, every X minutes.
                var appToken = this.GetPrimaryKey(out _).ToString();
                _application = await _portalInfoService.GetApplicationAsync(appToken);
            }
            catch (Exception ex)
            {
                // Do nothing, this action is best effort.
                _logger.LogError(ex, $"Portal error | {ex.Message}");
            }
        }

        public async Task<DBWS_Application> GetAsync()
        {
            var appToken = this.GetPrimaryKey(out _).ToString();
            _application ??= await _portalInfoService.GetApplicationAsync(appToken);

            return _application;
        }

        public async Task<DBWS_Application> SubscribeAndGetAsync(IApplicationService observer)
        {
            // Subscribe to changes (or renew subscription) to receive messages.
            _subsManager.Subscribe(observer, observer);

            // Return application
            return await GetAsync();
        }

        public async Task ResetStateAsync()
        {
            // Clear cache
            _application = null;

            // Notify observers
            var appToken = this.GetPrimaryKey(out _).ToString();
            await _subsManager.Notify(s => s.ApplicationChangedAsync(appToken));
        }
    }
}
