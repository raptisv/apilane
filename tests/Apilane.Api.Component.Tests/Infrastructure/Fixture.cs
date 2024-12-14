using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using System;

namespace CasinoService.ComponentTests.Infrastructure
{
    public class Fixture
    {
        public IClusterClient ClusterClient;
        public ApiConfiguration ApiConfiguration;
        public IPortalInfoService MockIPortalInfoService;
        public IApplicationService MockIApplicationService;

        public Fixture(IServiceProvider services)
        {
            ClusterClient = services.GetService<IClusterClient>() ?? throw new ArgumentNullException(nameof(IClusterClient));
            ApiConfiguration = services.GetService<ApiConfiguration>() ?? throw new ArgumentNullException(nameof(ApiConfiguration));
            MockIPortalInfoService = services.GetService<IPortalInfoService>() ?? throw new ArgumentNullException(nameof(IPortalInfoService));
            MockIApplicationService = services.GetService<IApplicationService>() ?? throw new ArgumentNullException(nameof(IApplicationService));
        }        
    }
}
