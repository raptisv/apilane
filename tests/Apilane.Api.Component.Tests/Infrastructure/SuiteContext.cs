using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using FakeItEasy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
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

            // appsettings.Development.json is not committed (it holds secrets). When it is absent,
            // e.g. on a fresh clone or CI, supply the required settings through environment
            // variables so the suite is self-contained. When a developer has the file, keep using it.
            if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.Development.json")))
            {
                var filesPath = Path.Combine(Path.GetTempPath(), "apilane-component-tests", "Files");
                Directory.CreateDirectory(filesPath);

                SetIfMissing("Url", "http://127.0.0.1:5001");
                SetIfMissing("PortalUrl", "http://localhost:5000");
                SetIfMissing("FilesPath", filesPath);
                SetIfMissing("InstallationKey", "component-tests-installation-key-not-a-secret");
            }

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

            var apiConfiguration = Factory.Services.GetRequiredService<ApiConfiguration>();

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

        private static void SetIfMissing(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            {
                Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
            }
        }
    }
}