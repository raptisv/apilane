using Apilane.Api.Component.Tests.Extensions;
using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common.Enums;
using Apilane.Net.Models.Enums;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// Email/ConfirmEmail and Email/ResetPassword spend a one-time token, so they have the allowance of the other
    /// email endpoints: an application-wide one, taken before the token is looked up. In a class of its own: the
    /// allowance is kept per application, and the tests of a class share one.
    /// </summary>
    public class EmailTokenEndpointsRateLimitTests : AppicationTestsBase
    {
        public EmailTokenEndpointsRateLimitTests() : base(SuiteContext.Shared)
        {
        }

        [Theory]
        [InlineData("ConfirmEmail")]
        [InlineData("ResetPassword")]
        public async Task Endpoint_OverTheAllowance_IsRefusedBeforeTheTokenIsLookedUp(string action)
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);
            var url = $"/api/Email/{action}?appToken={TestApplication.Token}";
            var body = new { Token = Guid.NewGuid().ToString(), Password = "Passw0rd!" };

            for (var i = 0; i < 60; i++)
            {
                var answer = await HttpClient.RequestAsync(HttpMethod.Post, url, body);
                var text = await answer.Content.ReadAsStringAsync();

                Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
                Assert.Contains(ValidationError.VALIDATION.ToString(), text);
            }

            var limited = await HttpClient.RequestAsync(HttpMethod.Post, url, body);

            Assert.Equal(HttpStatusCode.BadRequest, limited.StatusCode);
            Assert.Contains(ValidationError.RATE_LIMIT_EXCEEDED.ToString(), await limited.Content.ReadAsStringAsync());
        }
    }
}
