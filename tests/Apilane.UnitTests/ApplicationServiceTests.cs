using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Grains;
using Apilane.Api.Core.Services;
using Apilane.Common.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FakeItEasy;
using Orleans;

namespace Apilane.UnitTests
{
    [TestClass]
    public class ApplicationServiceTests
    {
        private const string AppToken = "22222222-2222-2222-2222-222222222222";
        private const string OtherToken = "33333333-3333-3333-3333-333333333333";

        [TestMethod]
        public async Task GetAsync_UnknownToken_Should_Ask_Once_And_Answer_NotFound()
        {
            var clusterClient = A.Fake<IClusterClient>();
            var grain = GrainFor(clusterClient, AppToken, Task.FromResult<DBWS_Application?>(null));
            var service = CreateService(clusterClient);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var ex = await Assert.ThrowsExactlyAsync<ApilaneException>(() => service.GetAsync(AppToken));
                Assert.AreEqual(AppErrors.NOT_FOUND, ex.Error);
            }

            A.CallTo(() => grain.SubscribeAndGetAsync(A<IApplicationService>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("not-a-guid")]
        public async Task GetAsync_TokenNotAGuid_Should_Answer_NotFound_Without_Asking(string appToken)
        {
            var clusterClient = A.Fake<IClusterClient>();
            var service = CreateService(clusterClient);

            var ex = await Assert.ThrowsExactlyAsync<ApilaneException>(() => service.GetAsync(appToken));

            Assert.AreEqual(AppErrors.NOT_FOUND, ex.Error);
            A.CallTo(() => clusterClient.GetGrain<IApplicationGrain>(A<Guid>._, A<string?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetAsync_Concurrent_Requests_Should_Share_One_Load_And_Not_Block_Other_Applications()
        {
            var clusterClient = A.Fake<IClusterClient>();
            var pending = new TaskCompletionSource<DBWS_Application?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var slowGrain = GrainFor(clusterClient, AppToken, pending.Task);
            GrainFor(clusterClient, OtherToken, Task.FromResult<DBWS_Application?>(new DBWS_Application() { Token = OtherToken }));
            var service = CreateService(clusterClient);

            var waiting = Enumerable.Range(0, 5).Select(_ => service.GetAsync(AppToken)).ToList();

            // Another application loads while the first one is still loading
            Assert.AreEqual(OtherToken, (await service.GetAsync(OtherToken)).Token);

            pending.SetResult(new DBWS_Application() { Token = AppToken });
            var loaded = await Task.WhenAll(waiting);

            Assert.IsTrue(loaded.All(x => x.Token == AppToken));
            A.CallTo(() => slowGrain.SubscribeAndGetAsync(A<IApplicationService>._)).MustHaveHappenedOnceExactly();
        }

        private static IApplicationGrain GrainFor(IClusterClient clusterClient, string appToken, Task<DBWS_Application?> answer)
        {
            var grain = A.Fake<IApplicationGrain>();
            A.CallTo(() => grain.SubscribeAndGetAsync(A<IApplicationService>._)).Returns(answer);
            A.CallTo(() => clusterClient.GetGrain<IApplicationGrain>(Guid.Parse(appToken), A<string?>._)).Returns(grain);
            return grain;
        }

        private static ApplicationService CreateService(IClusterClient clusterClient)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>()
                {
                    ["Url"] = "http://localhost:5001",
                    ["PortalUrl"] = "http://portal.test/",
                    ["FilesPath"] = Path.GetTempPath(),
                    ["InstallationKey"] = "unit-tests-installation-key-not-a-secret"
                })
                .Build();

            return new ApplicationService(
                NullLogger<ApplicationService>.Instance,
                new ApiConfiguration(configuration),
                clusterClient,
                new MemoryCache(new MemoryCacheOptions()));
        }
    }
}
