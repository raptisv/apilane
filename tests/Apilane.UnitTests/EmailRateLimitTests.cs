using Apilane.Api.Core;
using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Common;
using Apilane.Common.Abstractions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Microsoft.Extensions.Configuration;
using FakeItEasy;

namespace Apilane.UnitTests
{
    [TestClass]
    public class EmailRateLimitTests
    {
        [TestMethod]
        public async Task EmailRequests_UniqueUnknownAddresses_Should_ShareAnAggregateBudgetBeforeLookingUpUsers()
        {
            var dataStore = A.Fake<IApplicationDataStoreFactory>();
            var api = CreateApi(dataStore);
            var application = CreateApplication();

            for (var i = 0; i < 60; i++)
            {
                if (i % 2 == 0)
                {
                    await api.ForgotPasswordAsync(application, $"unknown-{i}@example.com");
                }
                else
                {
                    await api.RequestConfirmationAsync(application, $"unknown-{i}@example.com");
                }
            }

            var forgot = await Assert.ThrowsAsync<ApilaneException>(() => api.ForgotPasswordAsync(application, "another@example.com"));
            var confirmation = await Assert.ThrowsAsync<ApilaneException>(() => api.RequestConfirmationAsync(application, "different@example.com"));

            Assert.AreEqual(AppErrors.RATE_LIMIT_EXCEEDED, forgot.Error);
            Assert.AreEqual(AppErrors.RATE_LIMIT_EXCEEDED, confirmation.Error);
            Assert.AreEqual(60, Fake.GetCalls(dataStore).Count(x => x.Method.Name == nameof(IApplicationDataStoreFactory.GetPagedDataAsync)));

            // One application's exhausted aggregate budget must not block another application.
            await api.ForgotPasswordAsync(CreateApplication(), "another@example.com");
            Assert.AreEqual(61, Fake.GetCalls(dataStore).Count(x => x.Method.Name == nameof(IApplicationDataStoreFactory.GetPagedDataAsync)));
        }

        [TestMethod]
        public async Task EmailRequests_SameAddressWithDifferentCase_Should_KeepPerAddressLimit()
        {
            var dataStore = A.Fake<IApplicationDataStoreFactory>();
            var api = CreateApi(dataStore);
            var application = CreateApplication();

            await api.ForgotPasswordAsync(application, "Person@example.com");
            var exception = await Assert.ThrowsAsync<ApilaneException>(() => api.ForgotPasswordAsync(application, "person@EXAMPLE.COM"));

            Assert.AreEqual(AppErrors.RATE_LIMIT_EXCEEDED, exception.Error);
            Assert.AreEqual(1, Fake.GetCalls(dataStore).Count(x => x.Method.Name == nameof(IApplicationDataStoreFactory.GetPagedDataAsync)));
        }

        private static DBWS_Application CreateApplication()
        {
            return new DBWS_Application
            {
                Token = Guid.NewGuid().ToString(),
                MailServer = "smtp.example.com",
                MailServerPort = 587,
                MailFromAddress = "sender@example.com",
                MailFromDisplayName = "Sender",
                MailUserName = "sender",
                MailPassword = "test-only"
            };
        }

        private static EmailAPI CreateApi(IApplicationDataStoreFactory dataStore)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Url"] = "https://api.example.com",
                ["PortalUrl"] = "https://portal.example.com",
                ["FilesPath"] = Path.GetTempPath(),
                ["InstallationKey"] = "unit-test-installation-key-not-a-secret"
            }).Build();

            A.CallTo(() => dataStore.GetPagedDataAsync(A<string>._, A<List<string>?>._, A<FilterData?>._, A<List<SortData>?>._, A<int>._, A<int>._))
                .Returns(Task.FromResult(new List<Dictionary<string, object?>>()));

            return new EmailAPI(new ApiConfiguration(configuration), A.Fake<IApplicationHelperService>(), A.Fake<IApplicationDataService>(),
                A.Fake<IApplicationEmailService>(), A.Fake<IEmailService>(), dataStore);
        }
    }
}
