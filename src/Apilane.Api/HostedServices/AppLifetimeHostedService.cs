using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Api.HostedServices
{
    public class AppLifetimeHostedService : IHostedService
    {
        private readonly ILogger<AppLifetimeHostedService> _logger;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly Silo _silo;

        public AppLifetimeHostedService(
            ILogger<AppLifetimeHostedService> logger,
            IHostApplicationLifetime hostApplicationLifetime,
            Silo silo)
        {
            _logger = logger;
            _hostApplicationLifetime = hostApplicationLifetime;
            _silo = silo;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _hostApplicationLifetime.ApplicationStarted.Register(async () => await OnStartedAsync());
            _hostApplicationLifetime.ApplicationStopping.Register(async () => await OnStoppingAsync(cancellationToken));
            _hostApplicationLifetime.ApplicationStopped.Register(async () => await OnStoppedAsync());

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private Task OnStartedAsync()
        {
            _logger.LogInformation("Application Lifetime --- Started");

            return Task.CompletedTask;
        }

        private async Task OnStoppingAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Application Lifetime --- Stopping");

            // Gracefull silo shutdown
            if (_silo is not null)
            {
                await _silo.StopAsync(cancellationToken);
            }
        }

        private Task OnStoppedAsync()
        {
            _logger.LogInformation("Application Lifetime --- Stopped");

            return Task.CompletedTask;
        }
    }
}
