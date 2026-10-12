using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common.Enums;
using CasinoService.ComponentTests.Infrastructure;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// The IP list of an application (Block or Allow) holds addresses, networks (CIDR) and ranges. The address of
    /// the caller is the first entry of X-Forwarded-For, so a test can be any address.
    /// </summary>
    public class ClientIpRangeTests : AppicationTestsBase
    {
        public ClientIpRangeTests() : base(SuiteContext.Shared)
        {
        }

        private async Task SetUpAsync(AppClientIPsLogics logic, string list)
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);

            TestApplication.ClientIPsLogic = (int)logic;
            TestApplication.ClientIPsValue = list;
            MockApplicationService(TestApplication);
        }

        /// <summary>
        /// Calls an endpoint of the application from an address. What the application answers does not matter, only
        /// whether the list let the call in: a refused call is SERVICE_UNAVAILABLE.
        /// </summary>
        private async Task<bool> IsLetInAsync(string clientAddress)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/Email/RequestConfirmation?appToken={TestApplication.Token}&email=nobody@test.com");
            request.Headers.Add("X-Forwarded-For", clientAddress);

            var response = await HttpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            using var json = JsonDocument.Parse(body);
            var code = json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("Code", out var value)
                ? value.GetString()
                : null;

            return code != "SERVICE_UNAVAILABLE";
        }

        [Theory]
        [InlineData("10.1.2.3", true)]
        [InlineData("10.1.255.255", true)]
        [InlineData("10.2.0.0", false)]
        [InlineData("203.0.113.15", true)]
        [InlineData("203.0.113.21", false)]
        [InlineData("2001:db8::7", true)]
        [InlineData("2001:db9::7", false)]
        [InlineData("198.51.100.9", true)]
        [InlineData("8.8.8.8", false)]
        public async Task Allow_AcceptsOnlyTheAddressesNetworksAndRangesOfTheList(string client, bool expected)
        {
            await SetUpAsync(AppClientIPsLogics.Allow, "10.1.0.0/16,203.0.113.10-203.0.113.20,2001:db8::/32,198.51.100.9");

            Assert.Equal(expected, await IsLetInAsync(client));
        }

        [Theory]
        [InlineData("10.1.2.3", false)]
        [InlineData("10.2.0.0", true)]
        [InlineData("203.0.113.15", false)]
        [InlineData("8.8.8.8", true)]
        public async Task Block_RefusesTheAddressesNetworksAndRangesOfTheList(string client, bool expected)
        {
            await SetUpAsync(AppClientIPsLogics.Block, "10.1.0.0/16,203.0.113.10-203.0.113.20");

            Assert.Equal(expected, await IsLetInAsync(client));
        }

        [Fact]
        public async Task GetClientAddress_ReportsTheAddressTheListSeesAndHowItWasFound()
        {
            await SetUpAsync(AppClientIPsLogics.Allow, "203.0.113.0/24");

            using var owner = new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/Application/GetClientAddress?appToken={TestApplication.Token}");
            request.Headers.Add("X-Forwarded-For", "203.0.113.7, 10.0.0.1");

            var response = await HttpClient.SendAsync(request);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("203.0.113.7", root.GetProperty("ClientAddress").GetString());
            Assert.Equal("ForwardedFor", root.GetProperty("Source").GetString());
            Assert.Equal("203.0.113.7, 10.0.0.1", root.GetProperty("ForwardedFor").GetString());
            Assert.True(root.GetProperty("ForwardedForHonoured").GetBoolean());

            // The test host sets nothing: the header of anyone is believed, which is what the Portal warns about
            Assert.Equal("NotConfigured", root.GetProperty("Mode").GetString());
            Assert.Empty(root.GetProperty("TrustedProxies").EnumerateArray());
        }

        [Fact]
        public async Task AnEntryWrittenBeforeRanges_StillMatchesAsText()
        {
            await SetUpAsync(AppClientIPsLogics.Allow, " 10.0.0.1, ,10.0.0.2");

            Assert.True(await IsLetInAsync("10.0.0.2"));
            Assert.False(await IsLetInAsync("10.0.0.3"));
        }
    }
}
