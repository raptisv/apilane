using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// An account created before agent keys may still have a password and a valid login cookie.
    /// Agent restrictions follow the account's address, regardless of how it authenticated.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class LegacyAgentSessionApiTests
    {
        private readonly PortalFactory _portal;

        public LegacyAgentSessionApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        [Theory]
        [InlineData("GET", "/api/v1/session/api-token")]
        [InlineData("GET", "/api/v1/admin/users")]
        [InlineData("GET", "/api/v1/admin/settings")]
        [InlineData("DELETE", "/api/v1/session")]
        [InlineData("POST", "/api/v1/session")]
        [InlineData("POST", "/api/v1/account")]
        [InlineData("POST", "/api/v1/account/password-resets")]
        [InlineData("PUT", "/api/v1/account/password")]
        public async Task Agent_Cookie_Should_Keep_Permanent_Restrictions_Even_With_A_Legacy_Admin_Role(string method, string path)
        {
            // Old rows can have both a password and an administrator role, although agents created
            // through today's API have neither. A valid cookie must not restore those privileges.
            var legacy = await CreateLegacyAgentAsync(administrator: true);
            Assert.Null(legacy.Client.DefaultRequestHeaders.Authorization);
            var session = await (await legacy.Client.GetAsync("/api/v1/session")).ReadJsonAsync<SessionResponse>();
            Assert.Equal(legacy.Email, session.Email);
            Assert.False(session.IsAdmin);

            var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method is "POST" or "PUT")
            {
                // Authentication restrictions run before binding: these actions must be refused,
                // rather than attempted and rejected later for an incomplete body.
                request.Content = new { }.ToJsonContent();
            }

            var response = await legacy.Client.SendAsync(request);
            await AssertForbiddenAsync(response);
            Assert.Equal(PortalAgent.RefusedMessage, (await response.ReadJsonAsync<ErrorResponse>()).Message);
            Assert.Empty(_portal.ApiServer.Requests);

            // Refusing DELETE session must not revoke the existing session as a side effect.
            Assert.Equal(HttpStatusCode.OK, (await legacy.Client.GetAsync("/api/v1/session")).StatusCode);
        }

        [Fact]
        public async Task Agent_Cookie_Should_Use_Application_Grants_And_Notice_Revocation_Immediately()
        {
            var legacy = await CreateLegacyAgentAsync(administrator: false);
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var owner = await _portal.CreateSignedInClientAsync(ownerEmail, ownerPassword);
            var server = await _portal.CreateServerAsync();
            var app = (await _portal.CreateApplicationAsync(server.ID, ownerEmail, "legacy-agent-permissions", legacy.Email)).Application;
            var url = $"/api/v1/applications/{app.Token}";
            var collaboratorId = await _portal.WithDbContextAsync(db => db.Collaborations
                .Where(x => x.AppID == app.ID && x.UserEmail == legacy.Email).Select(x => x.ID).SingleAsync());
            var policyUrl = $"{url}/collaborators/{collaboratorId}/permissions";

            // A share predating the policy table gets the same read-only defaults through cookies.
            Assert.Equal(HttpStatusCode.OK, (await legacy.Client.GetAsync($"{url}/entities")).StatusCode);
            var initial = await (await legacy.Client.GetAsync($"{url}/permissions")).ReadJsonAsync<ApplicationPermissionsResponse>();
            Assert.True(initial.IsAgent);
            await AssertForbiddenAsync(await legacy.Client.PutAsync($"{url}/status", new { Online = false }.ToJsonContent()));
            await AssertForbiddenAsync(await legacy.Client.GetAsync($"{url}/connection-info"));
            await AssertForbiddenAsync(await legacy.Client.DeleteAsync(url));
            Assert.Empty(_portal.ApiServer.Requests);

            var grant = await owner.PutAsync(policyUrl, new
            {
                Permissions = new[]
                {
                    new AgentPermissionGrant { Resource = "application", Write = true },
                    new AgentPermissionGrant { Resource = "entities", Read = true }
                }
            }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");

            var allowed = await legacy.Client.PutAsync($"{url}/status", new { Online = false }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.False((await allowed.ReadJsonAsync<ApplicationResponse>()).Online);
            Assert.Single(_portal.ApiServer.Requests);

            var revoke = await owner.PutAsync(policyUrl, new { Permissions = Array.Empty<AgentPermissionGrant>() }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
            _portal.ResetApiServer();
            await AssertForbiddenAsync(await legacy.Client.GetAsync($"{url}/entities"));
            await AssertForbiddenAsync(await legacy.Client.PutAsync($"{url}/status", new { Online = true }.ToJsonContent()));
            Assert.Equal(HttpStatusCode.OK, (await legacy.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await legacy.Client.GetAsync($"{url}/permissions")).StatusCode);
            Assert.False(await _portal.WithDbContextAsync(db => db.Applications.Where(x => x.ID == app.ID).Select(x => x.Online).SingleAsync()));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Agent_Cookie_With_Legacy_Admin_Role_And_Write_Grants_Should_Not_Change_System_Constraints(bool throughImport)
        {
            var legacy = await CreateLegacyAgentAsync(administrator: true);
            var scene = await EntityScene.CreateAsync(_portal);
            var shared = await scene.Owner.PostAsync($"{scene.AppUrl}/collaborators", new
            {
                legacy.Email,
                Permissions = new[]
                {
                    new AgentPermissionGrant { Resource = "entities", Read = true, Write = true },
                    new AgentPermissionGrant { Resource = "schema", Read = true, Write = true }
                }
            }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, shared.StatusCode);

            // These rights authorize ordinary entity constraints and imports. The separate human
            // administrator rule must still refuse a system entity, despite the old role claim.
            var before = await scene.LoadEntityAsync("Users");
            var beforeCount = (await scene.LoadEntitiesAsync()).Count;
            HttpResponseMessage response;
            if (throughImport)
            {
                response = await legacy.Client.PostAsync($"{scene.AppUrl}/schema-import", new
                {
                    Entities = new[]
                    {
                        new { Name = "Products", IsNew = true, Constraints = Array.Empty<object>() },
                        new { Name = "Users", IsNew = false, Constraints = new object[] { new { TypeID = 1, Properties = "Nickname" } } }
                    }
                }.ToJsonContent());
            }
            else
            {
                response = await legacy.Client.PutAsync(scene.ConstraintsUrl("Users"), new
                {
                    Constraints = new[] { new { Type = "Unique", Properties = new[] { "Nickname" } } }
                }.ToJsonContent());
            }

            await AssertForbiddenAsync(response);
            Assert.Equal("Only an administrator can change the constraints of a system entity.",
                (await response.ReadJsonAsync<ErrorResponse>()).Message);
            Assert.Equal(before.EntConstraints, (await scene.LoadEntityAsync("Users")).EntConstraints);
            Assert.Equal(beforeCount, (await scene.LoadEntitiesAsync()).Count);
            Assert.Empty(await scene.AuditRowsAsync(legacy.Email));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        private async Task<(string Email, HttpClient Client)> CreateLegacyAgentAsync(bool administrator)
        {
            var (oldEmail, password) = administrator
                ? await _portal.CreateAdminUserAsync()
                : await _portal.CreateUserAsync();
            var email = $"legacy-{Guid.NewGuid():N}@agent.local";
            await _portal.WithDbContextAsync(async db =>
            {
                var user = await db.Users.SingleAsync(x => x.Email == oldEmail);
                user.Email = email;
                user.UserName = email;
                user.NormalizedEmail = email.ToUpperInvariant();
                user.NormalizedUserName = email.ToUpperInvariant();
                return await db.SaveChangesAsync();
            });

            return (email, await _portal.CreateSignedInClientAsync(email, password));
        }

        private static async Task AssertForbiddenAsync(HttpResponseMessage response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"Expected 403, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }
    }
}
