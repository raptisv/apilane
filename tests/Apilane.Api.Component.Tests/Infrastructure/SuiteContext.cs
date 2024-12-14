using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using FakeItEasy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

namespace CasinoService.ComponentTests.Infrastructure
{
    public class SuiteContext : IDisposable
    {
        public WebApplicationFactory<Apilane.Api.Program> Factory { get; }
        public Fixture Fixture { get; }
        public HttpClient HttpClient { get; }

        public SuiteContext()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development", EnvironmentVariableTarget.Process);

            var factory = new WebApplicationFactory<Apilane.Api.Program>();

            Factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var _mockPortalInfoService = A.Fake<IPortalInfoService>();
                    services.AddSingleton<IPortalInfoService>((s) => _mockPortalInfoService);

                    var _mockApplicationService = A.Fake<IApplicationService>();
                    services.AddSingleton<IApplicationService>((s) => _mockApplicationService);
                });
            });

            var apiConfiguration = factory.Services.GetRequiredService<ApiConfiguration>();

            Factory.Server.BaseAddress = new Uri(apiConfiguration.Url);

            HttpClient = Factory.CreateClient(new WebApplicationFactoryClientOptions()
            {
                BaseAddress = new Uri(apiConfiguration.Url)
            });

            Fixture = new Fixture(Factory.Services);
        }

        public void Dispose()
        {
            Factory.Dispose();
        }
    }
}