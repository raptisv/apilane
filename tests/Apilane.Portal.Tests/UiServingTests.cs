using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// How the Portal serves the UI: the built files and the index page at the site root, next to
    /// the only addresses the Portal answers itself (/api, /swagger, /health and /metrics).
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class UiServingTests
    {
        private const string Marker = "ui-marker";

        // One host with a built UI for the whole class; it is disposed with the factory.
        private static WebApplicationFactory<Program>? _uiHost;

        private readonly PortalFactory _portal;

        public UiServingTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- The index page ----------

        [Theory]
        [InlineData("/")]
        [InlineData("/apps")]
        [InlineData("/apps/some-token/entities/Orders")]
        [InlineData("/account/login?returnUrl=%2Fswagger")]
        [InlineData("/admin/servers")]
        [InlineData("/Account/Login")]
        public async Task Address_That_Is_No_File_And_No_Server_Route_Should_Return_The_Index_Page(string url)
        {
            var response = await UiClient().GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.True(response.Headers.CacheControl?.NoCache);
            Assert.Contains(Marker, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Ui_Address_Should_Say_The_Ui_Is_Not_Built()
        {
            var host = _portal.CreateHostWithUi(null);

            var response = await _portal.CreateAnonymousClient(host).GetAsync("/apps");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("has not been built", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Ui_Address_Should_Refuse_A_Post_With_405()
        {
            var response = await UiClient().PostAsync("/admin/servers", null);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        // ---------- Files ----------

        [Theory]
        // Vite puts a content hash in the names under /assets.
        [InlineData("/assets/app-abc123.js", "public, max-age=31536000, immutable")]
        [InlineData("/favicon.ico", "no-cache")]
        [InlineData("/favicon.svg", "no-cache")]
        [InlineData("/apple-touch-icon.png", "no-cache")]
        [InlineData("/index.html", "no-cache")]
        public async Task Ui_File_Should_Be_Served_At_The_Site_Root_With_Its_Cache_Policy(string url, string cacheControl)
        {
            var response = await UiClient().GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(cacheControl, response.Headers.CacheControl?.ToString());
            Assert.NotEmpty(await response.Content.ReadAsStringAsync());
        }

        [Theory]
        [InlineData("/assets/missing.js")]
        // The folder the UI is built into is not an address.
        [InlineData("/ui/index.html")]
        [InlineData("/ui/assets/app-abc123.js")]
        // Only the UI is served as files, nothing else under the web root.
        [InlineData("/EmailTemplates/FORGOT_PASSWORD.html")]
        public async Task Address_Of_A_File_That_Is_Not_In_The_Ui_Should_Return_404(string url)
        {
            var response = await UiClient().GetAsync(url);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
        }

        // ---------- The Portal's own addresses ----------

        [Fact]
        public async Task Server_Routes_Should_Answer_Themselves_Next_To_The_Ui()
        {
            var client = UiClient();

            var liveness = await client.GetAsync("/health/liveness");
            Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
            Assert.DoesNotContain(Marker, await liveness.Content.ReadAsStringAsync());

            var readiness = await client.GetAsync("/health/readiness");
            Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
            Assert.DoesNotContain(Marker, await readiness.Content.ReadAsStringAsync());

            var metrics = await client.GetAsync("/metrics");
            Assert.Equal(HttpStatusCode.OK, metrics.StatusCode);
            Assert.Equal("text/plain", metrics.Content.Headers.ContentType?.MediaType);

            // No installation key.
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/internal/applications/x")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/internal/applications/x/access")).StatusCode);

            var session = await client.GetAsync("/api/v1/session");
            Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await session.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Theory]
        [InlineData("/health")]
        [InlineData("/health/nope")]
        [InlineData("/metrics/nope")]
        public async Task Unknown_Address_Under_A_Server_Path_Should_Return_404_Not_The_Index_Page(string url)
        {
            var response = await UiClient().GetAsync(url);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
        }

        [Theory]
        [InlineData("GET", "/api")]
        [InlineData("GET", "/api/nope")]
        [InlineData("GET", "/api/internal")]
        [InlineData("GET", "/api/internal/nope")]
        [InlineData("GET", "/api/v1/does-not-exist")]
        [InlineData("POST", "/api/v1/does-not-exist")]
        public async Task Unknown_Api_Address_Should_Return_404_Json_Not_The_Index_Page(string method, string url)
        {
            var response = await UiClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(PortalErrorCode.NotFound, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        // ---------- /swagger and the sign-in page ----------

        [Fact]
        public async Task Swagger_Signed_Out_Should_Redirect_To_The_Sign_In_Page_Of_The_Ui()
        {
            var client = UiClient();

            var response = await client.GetAsync("/swagger/index.html");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

            // 'returnUrl' in exactly this spelling: it is what the sign-in page reads.
            var location = response.Headers.Location?.OriginalString ?? string.Empty;
            Assert.EndsWith("/account/login?returnUrl=%2Fswagger%2Findex.html", location);

            // The address of the redirect is a screen of the UI.
            var signInPage = await client.GetAsync(location);
            Assert.Equal(HttpStatusCode.OK, signInPage.StatusCode);
            Assert.Contains(Marker, await signInPage.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Swagger_Signed_In_Should_Answer_Itself_And_404_For_An_Unknown_Address()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var client = (await _portal.SignInAsync(email, password, UiHost())).Client;

            var document = await client.GetAsync("/swagger/v1/swagger.json");
            Assert.Equal(HttpStatusCode.OK, document.StatusCode);
            Assert.Equal("application/json", document.Content.Headers.ContentType?.MediaType);

            var unknown = await client.GetAsync("/swagger/nope");
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Empty(await unknown.Content.ReadAsStringAsync());
        }

        private WebApplicationFactory<Program> UiHost()
        {
            return _uiHost ??= _portal.CreateHostWithUi($"<html>{Marker}</html>");
        }

        private HttpClient UiClient()
        {
            return _portal.CreateAnonymousClient(UiHost());
        }
    }
}
