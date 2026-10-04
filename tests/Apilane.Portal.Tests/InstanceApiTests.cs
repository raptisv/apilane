using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// The instance has one settings row, shared by every test class. Each test here sets the
    /// values it needs and the seeded state is put back afterwards.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class InstanceApiTests : IAsyncLifetime
    {
        private const string Url = "/api/v1/instance";

        private readonly PortalFactory _portal;

        public InstanceApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            return _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: false);
        }

        [Fact]
        public async Task Get_Anonymous_Should_Return_The_Instance_Without_The_Csrf_Header()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: false);

            var client = _portal.CreateAnonymousClient();
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);

            // The strict reader refuses any property InstanceResponse does not have.
            var instance = await response.ReadJsonAsync<InstanceResponse>();
            Assert.Equal("Apilane tests", instance.InstanceTitle);
            Assert.True(instance.AllowRegister);
            Assert.False(instance.IsMailSetup);
        }

        [Fact]
        public async Task Get_Should_Follow_The_Settings_And_Carry_No_Secret()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: false, mailConfigured: true);

            var settings = await _portal.WithDbContextAsync(db => db.GlobalSettings.AsNoTracking().SingleAsync());

            var response = await _portal.CreateAnonymousClient().GetAsync(Url);
            var body = await response.Content.ReadAsStringAsync();

            var instance = await response.ReadJsonAsync<InstanceResponse>();
            Assert.False(instance.AllowRegister);
            Assert.True(instance.IsMailSetup);

            Assert.DoesNotContain(settings.InstallationKey, body);
            Assert.DoesNotContain(settings.MailPassword ?? "no-mail-password", body);
            Assert.DoesNotContain(settings.MailServer ?? "no-mail-server", body);
        }

        [Fact]
        public async Task Get_Signed_In_Should_Work_Too()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
