using Apilane.Api.Component.Tests.Extensions;
using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Api.Core.Services;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using FakeItEasy;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// An application token is a GUID, and a GUID can be written in several ways (upper case, braces,
    /// no dashes). The API resolves every spelling to the same application, so whatever else is decided
    /// per application (who owns it, how many requests it may take) must not depend on the spelling.
    /// </summary>
    public class AppTokenSpellingTests : AppicationTestsBase
    {
        public AppTokenSpellingTests() : base(SuiteContext.Shared)
        {
        }

        private const string EntityName = "Notes";
        private const string EndpointName = "ping";

        private string Spell(string format)
        {
            var token = Guid.Parse(TestApplication.Token);

            return format == "upper"
                ? token.ToString().ToUpperInvariant()
                : token.ToString(format);
        }

        // The situation on a real installation: the API resolves any spelling of the GUID to the
        // application, and the Portal holds a second application row whose token is that other spelling,
        // owned by the caller (the Portal compares tokens as plain text).
        private void GivenThePortalSaysTheCallerOwns(string spelling)
        {
            A.CallTo(() => ApplicationServiceMock.GetAsync(spelling))
                .Returns(Task.FromResult(TestApplication));

            A.CallTo(() => ApplicationServiceMock.GetDbInfoAsync(spelling))
                .Returns(ValueTask.FromResult(TestApplication.ToDbInfo(ApiConfiguration.FilesPath)));

            A.CallTo(() => PortalInfoServiceMock.UserOwnsApplicationAsync(A<string>.Ignored, spelling))
                .Returns(Task.FromResult(true));
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpClient httpClient, string appToken, string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add(Globals.ApplicationTokenHeaderName, appToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "a-portal-user-token");

            return await httpClient.SendAsync(request);
        }

        [Theory]
        [InlineData("upper")]
        [InlineData("N")]
        [InlineData("B")]
        public async Task Owner_Of_Another_Spelling_Does_Not_Own_The_Application(string format)
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);

            // An entity nobody but the owner may read
            await AddEntityAsync(EntityName);

            var spelling = Spell(format);
            Assert.NotEqual(TestApplication.Token, spelling);
            GivenThePortalSaysTheCallerOwns(spelling);

            var httpClient = CreateHttpClient();
            var dataUrl = $"/api/Data/Get?entity={EntityName}";
            var ownerOnlyUrl = "/api/Application/GetSystemPropertiesAndConstraints?entityHasDifferentiationProperty=false";

            // The real owner reads the data and reaches the owner-only endpoints
            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                Assert.Equal(HttpStatusCode.OK, (await SendAsync(httpClient, TestApplication.Token, dataUrl)).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await SendAsync(httpClient, TestApplication.Token, ownerOnlyUrl)).StatusCode);
            }

            // Owning "another spelling" of the token is not owning the application
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(httpClient, spelling, dataUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(httpClient, spelling, ownerOnlyUrl)).StatusCode);
        }

        [Fact]
        public async Task RateLimit_Is_Shared_By_Every_Spelling_Of_The_Token_And_Of_The_Endpoint()
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);

            AddCustomEndpoint(EndpointName, "SELECT 1");

            var spelling = Spell("upper");
            A.CallTo(() => ApplicationServiceMock.GetAsync(spelling))
                .Returns(Task.FromResult(TestApplication));
            A.CallTo(() => ApplicationServiceMock.GetDbInfoAsync(spelling))
                .Returns(ValueTask.FromResult(TestApplication.ToDbInfo(ApiConfiguration.FilesPath)));

            var httpClient = CreateHttpClient();

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, EndpointName, type: SecurityTypes.CustomEndpoint,
                rateLimit: DBWS_Security.RateLimitItem.New(1, EndpointRateLimit.Per_Minute)))
            {
                ApplicationRateLimiter.GetOrCreate(TestApplication.Token).Reset(null, EntityAccess.GetRateLimitName(EndpointName, SecurityTypes.CustomEndpoint), SecurityActionType.get.ToString());

                // The one allowed request of this minute
                (await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(EndpointName)))
                    .Match(r => r, e => throw new Exception($"The first request was rejected | {e.Code} | {e.Message}"));

                (await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(EndpointName))).Match(
                    r => throw new Exception("The second request was not rate limited"),
                    e => Assert.Equal(ValidationError.RATE_LIMIT_EXCEEDED, e.Code));

                // Writing the endpoint name differently does not start a new allowance
                (await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(EndpointName.ToUpperInvariant()))).Match(
                    r => throw new Exception("Another spelling of the endpoint name was not rate limited"),
                    e => Assert.Equal(ValidationError.RATE_LIMIT_EXCEEDED, e.Code));

                // ...and neither does writing the application token differently
                var response = await SendAsync(httpClient, spelling, $"/api/Custom/{EndpointName}");
                var body = await response.Content.ReadAsStringAsync();

                Assert.False(response.IsSuccessStatusCode, "Another spelling of the application token was not rate limited");
                Assert.Contains(ValidationError.RATE_LIMIT_EXCEEDED.ToString(), body);
            }
        }

        [Fact]
        public async Task RateLimit_Applies_When_The_Entity_Parameter_Is_Repeated()
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);

            await AddEntityAsync(EntityName);

            var httpClient = CreateHttpClient();

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, EntityName,
                rateLimit: DBWS_Security.RateLimitItem.New(1, EndpointRateLimit.Per_Minute)))
            {
                ApplicationRateLimiter.GetOrCreate(TestApplication.Token).Reset(null, EntityName, SecurityActionType.get.ToString());

                // The one allowed request of this minute
                (await ApilaneService.GetDataAsync<Apilane.Net.Models.Data.DataItem>(DataGetListRequest.New(EntityName)))
                    .Match(r => r, e => throw new Exception($"The first request was rejected | {e.Code} | {e.Message}"));

                // The action reads the first value of a repeated parameter; the limiter must read the same one
                var response = await SendAsync(httpClient, TestApplication.Token, $"/api/Data/Get?entity={EntityName}&entity=x");
                var body = await response.Content.ReadAsStringAsync();

                Assert.False(response.IsSuccessStatusCode, "Repeating the entity parameter skipped the rate limit");
                Assert.Contains(ValidationError.RATE_LIMIT_EXCEEDED.ToString(), body);
            }
        }

        [Theory]
        [InlineData("upper")]
        [InlineData("B")]
        [InlineData("../not-a-guid")]
        public async Task Generate_With_A_Token_That_Is_Not_A_Canonical_Guid_Is_Rejected(string format)
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);

            // The token becomes a folder and a database file name on the API server
            var application = JsonSerializer.Deserialize<DBWS_Application>(JsonSerializer.Serialize(TestApplication))
                ?? throw new Exception("Could not copy the application");
            application.Token = format.StartsWith("..") ? format : Spell(format);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ApplicationNew/Generate")
            {
                Content = application.ToJsonData()
            };
            request.Headers.Add(Globals.InstallationKeyHeaderName, ApiConfiguration.InstallationKey);

            var response = await CreateHttpClient().SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.False(response.IsSuccessStatusCode, $"Generate accepted the token '{application.Token}' | {body}");
            Assert.Contains("Application token is not valid", body);
        }
    }
}
