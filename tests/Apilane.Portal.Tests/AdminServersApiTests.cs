using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class AdminServersApiTests
    {
        private const string Url = "/api/v1/admin/servers";

        private readonly PortalFactory _portal;

        public AdminServersApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- List ----------

        [Fact]
        public async Task List_Admin_Should_Return_The_Seeded_Server()
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var servers = await response.ReadJsonAsync<ListResponse<ServerResponse>>();
            var server = Assert.Single(servers.Data, x => x.ID == 1);

            Assert.Equal(servers.Data.Count, servers.Total);
            Assert.Equal("My server", server.Name);
            Assert.Equal(PortalFactory.ApiUrl, server.ServerUrl);
        }

        [Fact]
        public async Task List_Should_Be_In_The_Order_Added_And_Count_Applications()
        {
            // Names in the opposite order of the IDs, so a sort by name would fail.
            var first = await _portal.CreateServerAsync("zzz");
            var second = await _portal.CreateServerAsync("aaa");
            await _portal.CreateApplicationAsync(second.ID);
            await _portal.CreateApplicationAsync(second.ID);

            var client = await _portal.CreateAdminClientAsync();

            var servers = (await (await client.GetAsync(Url)).ReadJsonAsync<ListResponse<ServerResponse>>()).Data;

            var firstIndex = servers.FindIndex(x => x.ID == first.ID);
            var secondIndex = servers.FindIndex(x => x.ID == second.ID);

            Assert.True(firstIndex >= 0 && firstIndex < secondIndex, "Servers are not listed in the order they were added.");
            Assert.Equal(0, servers[firstIndex].ApplicationCount);
            Assert.Equal(2, servers[secondIndex].ApplicationCount);
        }

        [Fact]
        public async Task List_Anonymous_Should_Return_401_Json()
        {
            var client = _portal.CreateAnonymousClient();

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task List_User_Without_Admin_Role_Should_Return_403_Json()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task List_Admin_After_Signing_In_Elsewhere_Should_Return_401()
        {
            var (email, password) = await _portal.CreateAdminUserAsync();

            var first = await _portal.CreateSignedInClientAsync(email, password);
            await _portal.CreateSignedInClientAsync(email, password);

            // The first cookie still carries the Admin role; only the session check can refuse it.
            var response = await first.GetAsync(Url);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task List_User_Without_Admin_Role_After_Signing_In_Elsewhere_Should_Return_401_Not_403()
        {
            var (email, password) = await _portal.CreateUserAsync();

            var first = await _portal.CreateSignedInClientAsync(email, password);
            await _portal.CreateSignedInClientAsync(email, password);

            var response = await first.GetAsync(Url);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        // ---------- Get ----------

        [Fact]
        public async Task Get_Should_Return_The_Server()
        {
            var seeded = await _portal.CreateServerAsync();
            await _portal.CreateApplicationAsync(seeded.ID);

            var client = await _portal.CreateAdminClientAsync();

            var response = await client.GetAsync($"{Url}/{seeded.ID}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var server = await response.ReadJsonAsync<ServerResponse>();
            Assert.Equal(seeded.ID, server.ID);
            Assert.Equal(seeded.Name, server.Name);
            Assert.Equal(seeded.ServerUrl, server.ServerUrl);
            Assert.Equal(1, server.ApplicationCount);
        }

        [Fact]
        public async Task Get_Unknown_Id_Should_Return_404()
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.GetAsync($"{Url}/{long.MaxValue}");

            await AssertNotFoundAsync(response);
        }

        [Fact]
        public async Task Get_Anonymous_Should_Return_401()
        {
            var response = await _portal.CreateAnonymousClient().GetAsync($"{Url}/1");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Get_User_Without_Admin_Role_Should_Return_403()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync($"{Url}/1");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ---------- Create ----------

        [Fact]
        public async Task Create_Should_Add_The_Server_And_An_Audit_Row()
        {
            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);
            var name = NewName();

            var response = await client.PostAsync(Url, new ServerRequest { Name = name, ServerUrl = "https://one.example.test" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.ReadJsonAsync<ServerResponse>();
            Assert.True(created.ID > 1);
            Assert.Equal(name, created.Name);
            Assert.Equal("https://one.example.test", created.ServerUrl);
            Assert.Equal(0, created.ApplicationCount);
            Assert.EndsWith($"{Url}/{created.ID}", response.Headers.Location?.ToString());

            // It can be read back.
            var read = await (await client.GetAsync($"{Url}/{created.ID}")).ReadJsonAsync<ServerResponse>();
            Assert.Equal(name, read.Name);

            var stored = await _portal.WithDbContextAsync(db => db.Servers.AsNoTracking().SingleAsync(x => x.ID == created.ID));
            Assert.True(stored.DateModified > DateTime.UtcNow.AddMinutes(-5));

            var audit = await FindAuditAsync(name, "Created");
            Assert.Equal(email, audit.UserEmail);
            Assert.Null(audit.AppID);
            Assert.Contains("https://one.example.test", audit.Changes);
        }

        [Theory]
        [InlineData("{\"ServerUrl\":\"https://one.example.test\"}", "Name", "Required")]
        [InlineData("{\"Name\":\"\",\"ServerUrl\":\"https://one.example.test\"}", "Name", "Required")]
        [InlineData("{\"Name\":\"   \",\"ServerUrl\":\"https://one.example.test\"}", "Name", "Required")]
        [InlineData("{\"Name\":\"x\"}", "ServerUrl", "Required")]
        [InlineData("{\"Name\":\"x\",\"ServerUrl\":\"\"}", "ServerUrl", "Required")]
        [InlineData("{\"Name\":\"x\",\"ServerUrl\":\"not a url\"}", "ServerUrl", "Not a valid url")]
        [InlineData("{\"Name\":\"x\",\"ServerUrl\":\"/relative/path\"}", "ServerUrl", "Not a valid url")]
        [InlineData("{\"Name\":\"x\",\"ServerUrl\":\"api.example.test\"}", "ServerUrl", "Not a valid url")]
        // Only http and https. The Razor page still accepts these.
        [InlineData("{\"Name\":\"x\",\"ServerUrl\":\"javascript:alert(1)\"}", "ServerUrl", "Not a valid url")]
        [InlineData("{\"Name\":\"x\",\"ServerUrl\":\"ftp://one.example.test\"}", "ServerUrl", "Not a valid url")]
        [InlineData("{\"Name\":\"x\",\"ServerUrl\":\"localhost:5000\"}", "ServerUrl", "Not a valid url")]
        public async Task Create_Invalid_Should_Return_400_With_The_Property(string body, string property, string message)
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PostAsync(Url, Json(body));

            await AssertValidationAsync(response, property, message);
        }

        [Fact]
        public async Task Create_Both_Missing_Should_Report_Both_Properties()
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PostAsync(Url, Json("{}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.NotNull(error.Errors);
            Assert.Contains(error.Errors, x => x.Property == "Name" && x.Message == "Required");
            Assert.Contains(error.Errors, x => x.Property == "ServerUrl" && x.Message == "Required");
        }

        [Fact]
        public async Task Create_Invalid_Should_Not_Add_A_Server()
        {
            var client = await _portal.CreateAdminClientAsync();
            var name = NewName();

            await client.PostAsync(Url, new ServerRequest { Name = name, ServerUrl = "not a url" }.ToJsonContent());

            Assert.False(await _portal.WithDbContextAsync(db => db.Servers.AnyAsync(x => x.Name == name)));
        }

        [Theory]
        [InlineData("https://x.example.test/", false)]
        [InlineData("HTTP://x.example.test:5000/api", false)]
        [InlineData("https://x.example.test", true)]
        public async Task Create_Should_Store_The_Values_As_Sent(string serverUrl, bool padName)
        {
            // Like the Razor page: no trimming and no trailing-slash clean-up.
            var client = await _portal.CreateAdminClientAsync();
            var name = padName ? $" {NewName()} " : NewName();

            var response = await client.PostAsync(Url, new ServerRequest { Name = name, ServerUrl = serverUrl }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var id = (await response.ReadJsonAsync<ServerResponse>()).ID;
            var stored = await _portal.WithDbContextAsync(db => db.Servers.AsNoTracking().SingleAsync(x => x.ID == id));
            Assert.Equal(name, stored.Name);
            Assert.Equal(serverUrl, stored.ServerUrl);
        }

        [Fact]
        public async Task Create_Duplicate_Name_And_Url_Should_Be_Allowed()
        {
            var client = await _portal.CreateAdminClientAsync();
            var request = new ServerRequest { Name = NewName(), ServerUrl = "https://one.example.test" };

            var first = await client.PostAsync(Url, request.ToJsonContent());
            var second = await client.PostAsync(Url, request.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            Assert.NotEqual((await first.ReadJsonAsync<ServerResponse>()).ID, (await second.ReadJsonAsync<ServerResponse>()).ID);
        }

        [Fact]
        public async Task Create_With_A_Revoked_Session_Should_Return_401_And_Add_Nothing()
        {
            var (email, password) = await _portal.CreateAdminUserAsync();

            var first = await _portal.CreateSignedInClientAsync(email, password);
            await _portal.CreateSignedInClientAsync(email, password);
            var name = NewName();

            var response = await first.PostAsync(Url, new ServerRequest { Name = name, ServerUrl = "https://one.example.test" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
            Assert.False(await _portal.WithDbContextAsync(db => db.Servers.AnyAsync(x => x.Name == name)));
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("PUT")]
        [InlineData("DELETE")]
        public async Task Id_That_Is_Not_A_Number_Should_Return_404_Json(string method)
        {
            var client = await _portal.CreateAdminClientAsync();
            var request = new HttpRequestMessage(new HttpMethod(method), $"{Url}/abc");

            if (method == "PUT")
            {
                request.Content = ValidRequest();
            }

            // No route matches, so the API fallback answers: NOT_FOUND without an Entity.
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(PortalErrorCode.NotFound, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        public async Task Body_That_Is_Not_Json_Should_Return_415_With_The_Error_Body(string method)
        {
            var seeded = await _portal.CreateServerAsync();
            var client = await _portal.CreateAdminClientAsync();
            var url = method == "POST" ? Url : $"{Url}/{seeded.ID}";

            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
            {
                Content = new StringContent("Name=x", Encoding.UTF8, "text/plain")
            });

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Error, error.Code);
            Assert.False(string.IsNullOrWhiteSpace(error.TraceId));
        }

        [Fact]
        public async Task Create_Without_The_Csrf_Header_Should_Return_403_And_Add_Nothing()
        {
            var client = await _portal.CreateAdminClientAsync();
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);
            var name = NewName();

            var response = await client.PostAsync(Url, new ServerRequest { Name = name, ServerUrl = "https://one.example.test" }.ToJsonContent());

            await AssertCsrfRejectedAsync(response);
            Assert.False(await _portal.WithDbContextAsync(db => db.Servers.AnyAsync(x => x.Name == name)));
        }

        [Fact]
        public async Task Create_Anonymous_Should_Return_401()
        {
            var response = await _portal.CreateAnonymousClient().PostAsync(Url, ValidRequest());

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task Create_User_Without_Admin_Role_Should_Return_403()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.PostAsync(Url, ValidRequest());

            await AssertNotAdminAsync(response);
        }

        // ---------- Update ----------

        [Fact]
        public async Task Update_Should_Change_Name_And_Url_Only_And_Add_An_Audit_Row()
        {
            var seeded = await _portal.CreateServerAsync();
            await _portal.CreateApplicationAsync(seeded.ID);
            var before = await _portal.WithDbContextAsync(db => db.Servers.AsNoTracking().SingleAsync(x => x.ID == seeded.ID));

            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);
            var name = NewName();

            var response = await client.PutAsync($"{Url}/{seeded.ID}", new ServerRequest { Name = name, ServerUrl = "https://two.example.test" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var updated = await response.ReadJsonAsync<ServerResponse>();
            Assert.Equal(seeded.ID, updated.ID);
            Assert.Equal(name, updated.Name);
            Assert.Equal("https://two.example.test", updated.ServerUrl);
            Assert.Equal(1, updated.ApplicationCount);

            var stored = await _portal.WithDbContextAsync(db => db.Servers.AsNoTracking().SingleAsync(x => x.ID == seeded.ID));
            Assert.Equal(name, stored.Name);
            Assert.Equal("https://two.example.test", stored.ServerUrl);
            // The Razor page leaves DateModified alone on an edit; so does the API.
            Assert.Equal(before.DateModified, stored.DateModified);

            var audit = await FindAuditAsync(name, "Modified");
            Assert.Equal(email, audit.UserEmail);
            Assert.Null(audit.AppID);
            Assert.Contains(seeded.Name, audit.Changes);
            Assert.Contains("https://two.example.test", audit.Changes);
        }

        [Fact]
        public async Task Update_With_The_Same_Values_Should_Succeed_Without_An_Audit_Row()
        {
            var seeded = await _portal.CreateServerAsync();
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PutAsync($"{Url}/{seeded.ID}", new ServerRequest { Name = seeded.Name, ServerUrl = seeded.ServerUrl }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(await _portal.WithDbContextAsync(db => db.AuditLogs.AnyAsync(x => x.EntityIdentifier == seeded.Name)));
        }

        [Theory]
        [InlineData("{\"ServerUrl\":\"https://one.example.test\"}", "Name", "Required")]
        [InlineData("{\"Name\":\"\",\"ServerUrl\":\"https://one.example.test\"}", "Name", "Required")]
        [InlineData("{\"Name\":\"   \",\"ServerUrl\":\"https://one.example.test\"}", "Name", "Required")]
        [InlineData("{\"Name\":\"x\"}", "ServerUrl", "Required")]
        [InlineData("{\"Name\":\"x\",\"ServerUrl\":\"\"}", "ServerUrl", "Required")]
        [InlineData("{\"Name\":\"x\",\"ServerUrl\":\"not a url\"}", "ServerUrl", "Not a valid url")]
        [InlineData("{\"Name\":\"x\",\"ServerUrl\":\"javascript:alert(1)\"}", "ServerUrl", "Not a valid url")]
        public async Task Update_Invalid_Should_Return_400_With_The_Property_And_Change_Nothing(string body, string property, string message)
        {
            var seeded = await _portal.CreateServerAsync();
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PutAsync($"{Url}/{seeded.ID}", Json(body));

            await AssertValidationAsync(response, property, message);

            var stored = await _portal.WithDbContextAsync(db => db.Servers.AsNoTracking().SingleAsync(x => x.ID == seeded.ID));
            Assert.Equal(seeded.Name, stored.Name);
            Assert.Equal(seeded.ServerUrl, stored.ServerUrl);
        }

        [Fact]
        public async Task Update_Unknown_Id_Should_Return_404()
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PutAsync($"{Url}/{long.MaxValue}", ValidRequest());

            await AssertNotFoundAsync(response);
        }

        [Fact]
        public async Task Update_Without_The_Csrf_Header_Should_Return_403_And_Change_Nothing()
        {
            var seeded = await _portal.CreateServerAsync();
            var client = await _portal.CreateAdminClientAsync();
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await client.PutAsync($"{Url}/{seeded.ID}", ValidRequest());

            await AssertCsrfRejectedAsync(response);

            var stored = await _portal.WithDbContextAsync(db => db.Servers.AsNoTracking().SingleAsync(x => x.ID == seeded.ID));
            Assert.Equal(seeded.Name, stored.Name);
        }

        [Fact]
        public async Task Update_Anonymous_Should_Return_401()
        {
            var response = await _portal.CreateAnonymousClient().PutAsync($"{Url}/1", ValidRequest());

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Update_User_Without_Admin_Role_Should_Return_403()
        {
            var seeded = await _portal.CreateServerAsync();
            var client = await _portal.CreateUserClientAsync();

            var response = await client.PutAsync($"{Url}/{seeded.ID}", ValidRequest());

            await AssertNotAdminAsync(response);
        }

        // ---------- Delete ----------

        [Fact]
        public async Task Delete_Should_Remove_The_Server_And_Add_An_Audit_Row()
        {
            var seeded = await _portal.CreateServerAsync();
            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var response = await client.DeleteAsync($"{Url}/{seeded.ID}");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.False(await _portal.WithDbContextAsync(db => db.Servers.AnyAsync(x => x.ID == seeded.ID)));
            await AssertNotFoundAsync(await client.GetAsync($"{Url}/{seeded.ID}"));

            var audit = await FindAuditAsync(seeded.Name, "Deleted");
            Assert.Equal(email, audit.UserEmail);
            Assert.Null(audit.AppID);
        }

        [Fact]
        public async Task Delete_Server_With_Applications_Should_Return_409_And_Keep_It()
        {
            var seeded = await _portal.CreateServerAsync();
            await _portal.CreateApplicationAsync(seeded.ID);
            await _portal.CreateApplicationAsync(seeded.ID);

            var client = await _portal.CreateAdminClientAsync();

            var response = await client.DeleteAsync($"{Url}/{seeded.ID}");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Conflict, error.Code);
            Assert.Equal("The server cannot be deleted since it has 2 Application bound on it", error.Message);
            Assert.Equal("Server", error.Entity);

            Assert.True(await _portal.WithDbContextAsync(db => db.Servers.AnyAsync(x => x.ID == seeded.ID)));
        }

        [Fact]
        public async Task Delete_Unknown_Id_Should_Return_404()
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.DeleteAsync($"{Url}/{long.MaxValue}");

            await AssertNotFoundAsync(response);
        }

        [Fact]
        public async Task Delete_Without_The_Csrf_Header_Should_Return_403_And_Keep_The_Server()
        {
            var seeded = await _portal.CreateServerAsync();
            var client = await _portal.CreateAdminClientAsync();
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await client.DeleteAsync($"{Url}/{seeded.ID}");

            await AssertCsrfRejectedAsync(response);
            Assert.True(await _portal.WithDbContextAsync(db => db.Servers.AnyAsync(x => x.ID == seeded.ID)));
        }

        [Fact]
        public async Task Delete_Anonymous_Should_Return_401()
        {
            var seeded = await _portal.CreateServerAsync();

            var response = await _portal.CreateAnonymousClient().DeleteAsync($"{Url}/{seeded.ID}");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(await _portal.WithDbContextAsync(db => db.Servers.AnyAsync(x => x.ID == seeded.ID)));
        }

        [Fact]
        public async Task Delete_User_Without_Admin_Role_Should_Return_403_And_Keep_The_Server()
        {
            var seeded = await _portal.CreateServerAsync();
            var client = await _portal.CreateUserClientAsync();

            var response = await client.DeleteAsync($"{Url}/{seeded.ID}");

            await AssertNotAdminAsync(response);
            Assert.True(await _portal.WithDbContextAsync(db => db.Servers.AnyAsync(x => x.ID == seeded.ID)));
        }

        // ---------- Helpers ----------

        private static string NewName()
        {
            return $"server-{Guid.NewGuid():N}";
        }

        private static HttpContent ValidRequest()
        {
            return new ServerRequest { Name = NewName(), ServerUrl = "https://one.example.test" }.ToJsonContent();
        }

        private static HttpContent Json(string body)
        {
            return new StringContent(body, Encoding.UTF8, "application/json");
        }

        private Task<PortalAuditLog> FindAuditAsync(string serverName, string action)
        {
            return _portal.WithDbContextAsync(db => db.AuditLogs
                .AsNoTracking()
                .SingleAsync(x => x.EntityType == "Server" && x.EntityIdentifier == serverName && x.Action == action));
        }

        private static async Task AssertValidationAsync(HttpResponseMessage response, string property, string message)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.NotNull(error.Errors);

            var detail = Assert.Single(error.Errors);
            Assert.Equal(property, detail.Property);
            Assert.Equal(message, detail.Message);
        }

        private static async Task AssertNotFoundAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.NotFound, error.Code);
            Assert.Equal("Server", error.Entity);
        }

        private static async Task AssertCsrfRejectedAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);
        }

        // A 403 for the missing role, not for the CSRF header.
        private static async Task AssertNotAdminAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.DoesNotContain(PortalCsrfFilter.HeaderName, error.Message);
        }
    }
}
