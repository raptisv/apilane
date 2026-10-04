using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Services;
using Apilane.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FakeItEasy;
using System.Net;
using System.Text;

namespace Apilane.UnitTests
{
    /// <summary>
    /// What the API server sends to the Portal's internal API (/api/internal). The Portal's side
    /// of the same addresses is tested in InternalApiTests of Apilane.Portal.Tests: the two must
    /// change together.
    /// </summary>
    [TestClass]
    public class PortalInfoServiceTests
    {
        private const string AppToken = "11111111-1111-1111-1111-111111111111";
        private const string InstallationKey = "unit-tests-installation-key-not-a-secret";

        [TestMethod]
        public async Task GetApplicationAsync_Should_Ask_The_Internal_Api_With_The_Key_In_A_Header()
        {
            // Arrange
            var portal = new RecordingHandler($"{{\"Token\":\"{AppToken}\",\"Name\":\"Shop\"}}");

            // Act
            var application = await CreateService(portal).GetApplicationAsync(AppToken);

            // Assert
            var request = portal.Requests.Single();
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual($"http://portal.test/api/internal/applications/{AppToken}", request.RequestUri?.AbsoluteUri);
            Assert.AreEqual(InstallationKey, request.Headers.GetValues(Globals.InstallationKeyHeaderName).Single());
            Assert.IsNull(request.Headers.Authorization);

            Assert.AreEqual(AppToken, application.Token);
            Assert.AreEqual("Shop", application.Name);
        }

        [TestMethod]
        [DataRow("true", true)]
        [DataRow("false", false)]
        public async Task UserOwnsApplicationAsync_Should_Ask_The_Access_Address_With_The_Key_And_The_User_Token_In_Headers(string answer, bool expected)
        {
            // Arrange
            var portal = new RecordingHandler(answer);

            // Act
            var result = await CreateService(portal).UserOwnsApplicationAsync("user-token", AppToken);

            // Assert
            Assert.AreEqual(expected, result);

            var request = portal.Requests.Single();
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual($"http://portal.test/api/internal/applications/{AppToken}/access", request.RequestUri?.AbsoluteUri);
            Assert.AreEqual(InstallationKey, request.Headers.GetValues(Globals.InstallationKeyHeaderName).Single());
            Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
            Assert.AreEqual("user-token", request.Headers.Authorization?.Parameter);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(" ")]
        [DataRow(".")]
        [DataRow("..")]
        [DataRow("a/b")]
        [DataRow("not-a-guid")]
        public async Task UserOwnsApplicationAsync_AppTokenNotAGuid_Should_Be_False_Without_Asking_The_Portal(string appToken)
        {
            // Arrange
            var portal = new RecordingHandler("true");

            // Act
            var result = await CreateService(portal).UserOwnsApplicationAsync("user-token", appToken);

            // Assert
            Assert.IsFalse(result);
            Assert.IsEmpty(portal.Requests);
        }

        private static PortalInfoService CreateService(RecordingHandler portal)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>()
                {
                    ["Url"] = "http://localhost:5001",
                    // With a trailing slash, as it is often configured.
                    ["PortalUrl"] = "http://portal.test/",
                    ["FilesPath"] = Path.GetTempPath(),
                    ["InstallationKey"] = InstallationKey
                })
                .Build();

            var clientFactory = A.Fake<IHttpClientFactory>();
            A.CallTo(() => clientFactory.CreateClient(PortalInfoService.HttpClientName))
                .ReturnsLazily(() => new HttpClient(portal, disposeHandler: false));

            return new PortalInfoService(
                NullLogger<PortalInfoService>.Instance,
                clientFactory,
                new ApiConfiguration(configuration),
                // Remembers nothing: every question reaches the Portal.
                A.Fake<IMemoryCache>());
        }

        /// <summary>
        /// Stands in for the Portal: answers every request with 200 and the given JSON, and keeps
        /// the requests it got.
        /// </summary>
        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly string _json;

            public RecordingHandler(string json)
            {
                _json = json;
            }

            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_json, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
