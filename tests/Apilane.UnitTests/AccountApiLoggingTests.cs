using Apilane.Api.Core;
using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Data.Abstractions;
using FakeItEasy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Apilane.UnitTests
{
    /// <summary>
    /// The link in a confirmation mail holds a one-time token. What the API logs about a confirmation that
    /// did not work must not hand that token, or a near miss of it, to whoever reads the logs.
    /// </summary>
    [TestClass]
    public class AccountApiLoggingTests
    {
        private readonly CapturingLogger<AccountAPI> _logger = new();
        private readonly IApplicationHelperService _helperService = A.Fake<IApplicationHelperService>();

        private AccountAPI CreateAccountApi()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Url"] = "http://localhost:5001",
                    ["PortalUrl"] = "http://portal.test",
                    ["FilesPath"] = Path.GetTempPath(),
                    ["InstallationKey"] = "test-installation-key"
                })
                .Build();

            return new AccountAPI(
                _logger,
                A.Fake<IApplicationDataService>(),
                A.Fake<IApplicationDataStoreFactory>(),
                A.Fake<IApplicationEmailService>(),
                new ApiConfiguration(configuration),
                _helperService,
                A.Fake<IClusterClient>());
        }

        [TestMethod]
        public async Task ConfirmAsync_UnknownToken_ReturnsNullAndLogsAWarning()
        {
            // Arrange
            var token = Guid.NewGuid().ToString();
            A.CallTo(() => _helperService.GetUserIdFromEmailConfitmationTokenAsync(token))
                .Returns(Task.FromResult<long?>(null));

            // Act
            var redirectUrl = await CreateAccountApi().ConfirmAsync(Guid.NewGuid().ToString(), token, "app", null);

            // Assert
            Assert.IsNull(redirectUrl);
            Assert.IsTrue(_logger.Entries.Any(x => x.Level == LogLevel.Warning), "A token that is not found is not logged.");
        }

        [TestMethod]
        public async Task ConfirmAsync_UnknownToken_DoesNotLogTheToken()
        {
            // Arrange
            var token = Guid.NewGuid().ToString();
            A.CallTo(() => _helperService.GetUserIdFromEmailConfitmationTokenAsync(token))
                .Returns(Task.FromResult<long?>(null));

            // Act
            await CreateAccountApi().ConfirmAsync(Guid.NewGuid().ToString(), token, "app", null);

            // Assert
            foreach (var entry in _logger.Entries)
            {
                if (entry.Text.Contains(token))
                {
                    Assert.Fail($"A {entry.Level} log entry contains the token: {entry.Message}");
                }
            }
        }
    }
}
