using Apilane.Api.Component.Tests;
using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Common;
using FakeItEasy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;

namespace CasinoService.ComponentTests.Infrastructure
{
    /// <summary>
    /// The API host and the database containers of a test run. There is one instance for the whole run
    /// (<see cref="Shared"/>): the API serves many applications at once, so the test classes run in
    /// parallel against the same host, each with its own application and databases. It lives until the
    /// test process exits; Testcontainers' resource reaper then removes the containers.
    /// </summary>
    public class SuiteContext
    {
        private static readonly Lazy<SuiteContext> _shared = new Lazy<SuiteContext>(() => new SuiteContext());

        public static SuiteContext Shared => _shared.Value;

        public WebApplicationFactory<Apilane.Api.Program> Factory { get; }
        public Fixture Fixture { get; }

        /// <summary>
        /// The database servers the tests run against, in Docker containers, with one database per test class.
        /// </summary>
        public DatabaseContainers Databases { get; } = new DatabaseContainers(
            typeof(AppicationTestsBase).Assembly.GetTypes()
                .Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(AppicationTestsBase)))
                .Select(AppicationTestsBase.GetDatabaseName));

        private SuiteContext()
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

            Fixture = new Fixture(Factory.Services);
        }

        /// <summary>
        /// Creates a client for one test. Clients are not shared: the SDK stores the application token in
        /// the client's default headers, so a shared client would send every class's requests to whichever
        /// application registered first.
        /// </summary>
        public HttpClient CreateHttpClient()
        {
            HttpClient client;

            // The factory keeps its clients in a plain list, and tests are constructed on several threads
            lock (Factory)
            {
                client = Factory.CreateClient(new WebApplicationFactoryClientOptions()
                {
                    BaseAddress = Factory.Server.BaseAddress
                });
            }

            client.DefaultRequestHeaders.Add(Globals.ClientIdHeaderName, Globals.ClientIdHeaderValuePortal);

            return client;
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
