using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class SessionApiTests
    {
        private const string Url = "/api/v1/session";

        private readonly PortalFactory _portal;

        public SessionApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        [Fact]
        public async Task Get_Anonymous_Should_Return_401_Json_Not_A_Redirect()
        {
            var client = _portal.CreateAnonymousClient();

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.True(response.Headers.CacheControl?.NoStore);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Unauthorized, error.Code);
            Assert.False(string.IsNullOrWhiteSpace(error.TraceId));
        }

        [Fact]
        public async Task Get_Seeded_Admin_Should_Return_The_Signed_In_User()
        {
            // The only test that signs in as the administrator the Portal seeds on first start.
            var client = await _portal.CreateSignedInClientAsync(PortalFactory.AdminEmail, PortalFactory.AdminPassword);

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);

            var session = await response.ReadJsonAsync<SessionResponse>();
            Assert.Equal(PortalFactory.AdminEmail, session.Email);
            Assert.True(session.IsAdmin);
            Assert.Equal("Apilane tests", session.InstanceTitle);
            Assert.False(string.IsNullOrWhiteSpace(session.Version));
        }

        [Fact]
        public async Task Get_User_Without_Admin_Role_Should_Not_Be_Admin()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var session = await (await client.GetAsync(Url)).ReadJsonAsync<SessionResponse>();

            Assert.Equal(email, session.Email);
            Assert.False(session.IsAdmin);
        }

        [Fact]
        public async Task Get_After_Signing_In_Elsewhere_Should_Return_401()
        {
            var (email, password) = await _portal.CreateUserAsync();

            var first = await _portal.CreateSignedInClientAsync(email, password);
            Assert.Equal(HttpStatusCode.OK, (await first.GetAsync(Url)).StatusCode);

            // A second sign-in replaces the stored session token; the first cookie is still
            // cryptographically valid but no longer names the current session.
            var second = await _portal.CreateSignedInClientAsync(email, password);
            Assert.Equal(HttpStatusCode.OK, (await second.GetAsync(Url)).StatusCode);

            var response = await first.GetAsync(Url);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task Cookie_Refresh_Should_Keep_The_Session_Token()
        {
            // Identity refreshes the principal of a cookie every ValidationInterval; zero makes it every request.
            var refreshingHost = _portal.CreateHost(services =>
                services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero));

            var (email, password) = await _portal.CreateUserAsync();
            var session = await _portal.SignInAsync(email, password, refreshingHost);
            var storedToken = await GetStoredTokenAsync(email);
            var refreshing = _portal.CreateCookielessClient(refreshingHost);

            // Two requests with the same cookie, as two browser tabs would send.
            var first = await refreshing.GetWithCookieAsync(Url, session.Cookie);
            var second = await refreshing.GetWithCookieAsync(Url, session.Cookie);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            // The refresh did happen: both answers carry a renewed cookie.
            var firstRenewed = first.GetSetCookieHeader();
            var secondRenewed = second.GetSetCookieHeader();
            Assert.False(string.IsNullOrEmpty(firstRenewed), "The cookie was not refreshed, so the test proves nothing.");
            Assert.False(string.IsNullOrEmpty(secondRenewed), "The cookie was not refreshed, so the test proves nothing.");

            Assert.Equal(storedToken, await GetStoredTokenAsync(email));

            // The shared host (same database and keys) does not refresh, so it shows what each
            // cookie is worth as it is: neither refresh revoked the other, or the original.
            var plain = _portal.CreateCookielessClient();

            Assert.Equal(HttpStatusCode.OK, (await plain.GetWithCookieAsync(Url, firstRenewed)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await plain.GetWithCookieAsync(Url, secondRenewed)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await plain.GetWithCookieAsync(Url, session.Cookie)).StatusCode);
        }

        [Fact]
        public async Task Cookie_Refresh_Should_Not_Revive_A_Revoked_Session()
        {
            var refreshingHost = _portal.CreateHost(services =>
                services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero));

            var (email, password) = await _portal.CreateUserAsync();
            var revoked = await _portal.SignInAsync(email, password, refreshingHost);
            var current = await _portal.SignInAsync(email, password, refreshingHost);
            var storedToken = await GetStoredTokenAsync(email);
            var refreshing = _portal.CreateCookielessClient(refreshingHost);

            // The refresh of the old cookie must neither give it a new token nor take the new session's.
            var response = await refreshing.GetWithCookieAsync(Url, revoked.Cookie);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(storedToken, await GetStoredTokenAsync(email));

            var plain = _portal.CreateCookielessClient();
            var refreshedCookie = response.GetSetCookieHeader();

            // The refresh did happen: the 401 still carries a re-issued cookie, and it is worthless.
            Assert.False(string.IsNullOrEmpty(refreshedCookie), "The cookie was not refreshed, so the test proves nothing.");
            Assert.Equal(HttpStatusCode.Unauthorized, (await plain.GetWithCookieAsync(Url, refreshedCookie)).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await plain.GetWithCookieAsync(Url, current.Cookie)).StatusCode);
        }

        // ---------- Sign in (POST) ----------

        [Fact]
        public async Task Post_Should_Sign_In_With_A_Persistent_Cookie_And_Return_The_Session()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var before = DateTime.UtcNow.AddSeconds(-1);
            var client = _portal.CreateAnonymousClient();

            var response = await client.PostAsync(Url, new { Email = email, Password = password }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.True(response.SetsPersistentCookie(), "The login cookie must be persistent.");
            Assert.DoesNotContain(password, await response.Content.ReadAsStringAsync());

            var session = await response.ReadJsonAsync<SessionResponse>();
            Assert.Equal(email, session.Email);
            Assert.False(session.IsAdmin);
            Assert.Equal("Apilane tests", session.InstanceTitle);
            Assert.False(string.IsNullOrWhiteSpace(session.Version));

            // The cookie works, and GET answers the same session.
            var get = await client.GetAsync(Url);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal(email, (await get.ReadJsonAsync<SessionResponse>()).Email);

            // What a sign-in stores: a session token and the time of the login.
            var stored = await _portal.WithDbContextAsync(db => db.Users.AsNoTracking().SingleAsync(x => x.Email == email));
            Assert.False(string.IsNullOrWhiteSpace(stored.AdminAuthToken));
            Assert.True(stored.LastLogin >= before, "LastLogin was not set by the sign-in.");
        }

        [Fact]
        public async Task Post_Admin_Should_Get_A_Cookie_That_Works_On_An_Admin_Endpoint()
        {
            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = _portal.CreateAnonymousClient();

            var response = await client.PostAsync(Url, new { Email = email, Password = password }.ToJsonContent());

            Assert.True((await response.ReadJsonAsync<SessionResponse>()).IsAdmin);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admin/servers")).StatusCode);
            Assert.True((await (await client.GetAsync(Url)).ReadJsonAsync<SessionResponse>()).IsAdmin);
        }

        [Fact]
        public async Task Post_Should_End_The_Older_Session_Of_The_Same_User()
        {
            var (email, password) = await _portal.CreateUserAsync();

            var first = await _portal.SignInAsync(email, password);
            var firstToken = await GetStoredTokenAsync(email);
            Assert.Equal(HttpStatusCode.OK, (await first.Client.GetAsync(Url)).StatusCode);

            // One active session per user: the second sign-in replaces the stored session token.
            var second = await _portal.SignInAsync(email, password);
            var secondToken = await GetStoredTokenAsync(email);

            Assert.False(string.IsNullOrWhiteSpace(firstToken));
            Assert.False(string.IsNullOrWhiteSpace(secondToken));
            Assert.NotEqual(firstToken, secondToken);
            Assert.Equal(HttpStatusCode.OK, (await second.Client.GetAsync(Url)).StatusCode);

            var response = await first.Client.GetAsync(Url);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task Post_Should_Give_The_Same_401_For_A_Wrong_Password_And_An_Unknown_Email()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var session = await _portal.SignInAsync(email, password);
            var client = _portal.CreateAnonymousClient();

            var wrongPassword = await client.PostAsync(Url, new { Email = email, Password = "not-the-password" }.ToJsonContent());
            var unknownEmail = await client.PostAsync(Url, new { Email = $"nobody-{Guid.NewGuid():N}@portal.test", Password = "not-the-password" }.ToJsonContent());

            foreach (var response in new[] { wrongPassword, unknownEmail })
            {
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(string.Empty, response.GetSetCookieHeader());

                var error = await response.ReadJsonAsync<ErrorResponse>();
                Assert.Equal(PortalErrorCode.Unauthorized, error.Code);
                Assert.Equal("Invalid login attempt.", error.Message);
                Assert.Null(error.Errors);
            }

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);

            // A failed sign-in does not end the session the user has.
            Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync(Url)).StatusCode);
        }

        [Theory]
        [InlineData("{}", "Email: Required|Password: Required")]
        [InlineData("{\"Email\":\"someone@portal.test\"}", "Password: Required")]
        [InlineData("{\"Password\":\"some-password\"}", "Email: Required")]
        [InlineData("{\"Email\":\"\",\"Password\":\"\"}", "Email: Required|Password: Required")]
        [InlineData("{\"Email\":\"not-an-email\",\"Password\":\"some-password\"}", "Email: Not a valid email address")]
        public async Task Post_With_Missing_Or_Malformed_Fields_Should_Return_400_VALIDATION(string body, string errors)
        {
            var client = _portal.CreateAnonymousClient();

            var response = await client.PostAsync(Url, new StringContent(body, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            // One entry per property, with its message.
            Assert.Equal(
                errors.Split('|'),
                (error.Errors ?? new List<ErrorDetail>()).Select(x => $"{x.Property}: {x.Message}").OrderBy(x => x));
        }

        [Fact]
        public async Task Post_Without_The_Csrf_Header_Should_Return_403_And_Not_Sign_In()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var client = _portal.CreateAnonymousClient();
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await client.PostAsync(Url, new { Email = email, Password = password }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
            Assert.Equal(string.Empty, response.GetSetCookieHeader());
            Assert.Null(await GetStoredTokenAsync(email));
        }

        // ---------- Sign out (DELETE) ----------

        [Fact]
        public async Task Delete_Should_Remove_The_Cookie_And_Make_A_Copy_Of_It_Worthless()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var session = await _portal.SignInAsync(email, password);
            var cookieless = _portal.CreateCookielessClient();

            Assert.Equal(HttpStatusCode.OK, (await cookieless.GetWithCookieAsync(Url, session.Cookie)).StatusCode);

            var response = await session.Client.DeleteAsync(Url);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            // The browser is told to drop the cookie...
            Assert.True(response.RemovesLoginCookie(), "Sign-out must tell the browser to drop the cookie.");
            Assert.Equal(HttpStatusCode.Unauthorized, (await session.Client.GetAsync(Url)).StatusCode);

            // ...and the session is over for anyone who kept a copy.
            Assert.Equal(HttpStatusCode.Unauthorized, (await cookieless.GetWithCookieAsync(Url, session.Cookie)).StatusCode);
            Assert.Null(await GetStoredTokenAsync(email));
        }

        [Fact]
        public async Task Delete_Without_A_Cookie_Should_Return_204()
        {
            var response = await _portal.CreateAnonymousClient().DeleteAsync(Url);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task Delete_With_A_Stale_Cookie_Should_Return_204_And_Leave_The_Newer_Session_Alone()
        {
            var (email, password) = await _portal.CreateUserAsync();

            var stale = await _portal.SignInAsync(email, password);
            var current = await _portal.SignInAsync(email, password);
            var currentToken = await GetStoredTokenAsync(email);

            var response = await stale.Client.DeleteAsync(Url);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            // Dropping the stale cookie is all the endpoint does here.
            Assert.True(response.RemovesLoginCookie(), "Sign-out must tell the browser to drop the cookie.");
            Assert.Equal(currentToken, await GetStoredTokenAsync(email));
            Assert.Equal(HttpStatusCode.OK, (await current.Client.GetAsync(Url)).StatusCode);
        }

        [Fact]
        public async Task Delete_Without_The_Csrf_Header_Should_Return_403_And_Keep_The_Session()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var session = await _portal.SignInAsync(email, password);
            session.Client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await session.Client.DeleteAsync(Url);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
            Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync(Url)).StatusCode);
        }

        // ---------- Rate limit ----------

        [Fact]
        public async Task Anonymous_Account_Endpoints_Should_Return_429_Over_The_Limit_And_Nothing_Else_Should()
        {
            var host = _portal.CreateHost(services =>
                services.Configure<PortalRateLimitOptions>(options => options.PermitLimit = 2));

            var client = _portal.CreateAnonymousClient(host);
            var credentials = new { Email = $"nobody-{Guid.NewGuid():N}@portal.test", Password = "not-the-password" };

            // The budget: two calls, both answered by the endpoint itself.
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(Url, credentials.ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(Url, credentials.ToJsonContent())).StatusCode);

            var response = await client.PostAsync(Url, credentials.ToJsonContent());

            Assert.Equal((HttpStatusCode)429, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.True(response.Headers.Contains("Retry-After"));

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal("TOO_MANY_REQUESTS", error.Code);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            Assert.False(string.IsNullOrWhiteSpace(error.TraceId));

            // Register and the reset request draw on the same budget.
            var register = await client.PostAsync("/api/v1/account", new { Email = credentials.Email, Password = "long-enough", ConfirmPassword = "long-enough" }.ToJsonContent());
            var resetRequest = await client.PostAsync("/api/v1/account/password-reset-requests", new { credentials.Email }.ToJsonContent());

            Assert.Equal((HttpStatusCode)429, register.StatusCode);
            Assert.Equal((HttpStatusCode)429, resetRequest.StatusCode);

            // The budget is per address: a second caller is not locked out by the first.
            var other = new HttpRequestMessage(HttpMethod.Post, Url) { Content = credentials.ToJsonContent() };
            other.Headers.Add("X-Forwarded-For", "10.0.0.2");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(other)).StatusCode);

            // Every other route is untouched.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/instance")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Url)).StatusCode);
        }

        private Task<string?> GetStoredTokenAsync(string email)
        {
            return _portal.WithDbContextAsync(db => db.Users
                .AsNoTracking()
                .Where(x => x.Email == email)
                .Select(x => x.AdminAuthToken)
                .SingleAsync());
        }
    }
}
