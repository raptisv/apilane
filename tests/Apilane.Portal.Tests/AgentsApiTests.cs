using Apilane.Common;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// Agents: users at @agent.local that call the API with a key instead of a session cookie.
    /// One test switches the instance mail on; the seeded state is put back after every test.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class AgentsApiTests : IAsyncLifetime
    {
        private const string AgentsUrl = "/api/v1/admin/agents";
        private const string UsersUrl = "/api/v1/admin/users";
        private const string ApplicationsUrl = "/api/v1/applications";
        private const string SessionUrl = "/api/v1/session";

        private readonly PortalFactory _portal;

        public AgentsApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        public Task InitializeAsync()
        {
            return _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: false);
        }

        public Task DisposeAsync()
        {
            return _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: false);
        }

        // ---------- Create ----------

        [Fact]
        public async Task Create_Should_Return_The_Key_Once_And_Store_Only_Its_Hash()
        {
            var admin = await _portal.CreateAdminClientAsync();
            var name = NewName();

            var response = await admin.PostAsync(AgentsUrl, new { Name = name }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.ReadJsonAsync<AgentCreatedResponse>();
            Assert.Equal($"{name}@agent.local", created.Email);

            // apl_{KeyId}_{Secret}: 12 characters, then 32 random bytes as base64url.
            var key = Regex.Match(created.Key, "^apl_([0-9a-f]{12})_([A-Za-z0-9_-]{43})$");
            Assert.True(key.Success, "The key does not have the shape apl_{KeyId}_{Secret}.");

            var secret = key.Groups[2].Value;

            // A normal user with a confirmed address and no password: nobody can sign in as it.
            var user = await _portal.WithDbContextAsync(db => db.Users.AsNoTracking().SingleAsync(x => x.Email == created.Email));
            Assert.Equal(created.ID, user.Id);
            Assert.True(user.EmailConfirmed);
            Assert.Null(user.PasswordHash);

            var signIn = await _portal.CreateAnonymousClient().PostAsync(SessionUrl, new { Email = created.Email, Password = created.Key }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);

            // Only the KeyId and a hash of the secret are stored.
            var stored = await _portal.WithDbContextAsync(db => db.AgentKeys.AsNoTracking().SingleAsync(x => x.UserId == created.ID));
            Assert.Equal(key.Groups[1].Value, stored.KeyId);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))), stored.SecretHash);

            // The list of users shows the agent, and nothing of its key. The strict reader fails on any extra property.
            var list = await admin.GetAsync(UsersUrl);
            var listed = Assert.Single((await list.ReadJsonAsync<ListResponse<UserResponse>>()).Data, x => x.ID == created.ID);
            Assert.Equal(created.Email, listed.Email);
            Assert.False(listed.IsAdmin);
            Assert.DoesNotContain(secret, await list.Content.ReadAsStringAsync());
        }

        [Theory]
        [InlineData("")]
        [InlineData("ab")]
        [InlineData("a-name-that-is-one-character-too-long-41c")]
        [InlineData("Bot")]
        [InlineData("my bot")]
        [InlineData("my_bot")]
        [InlineData("bot.one")]
        [InlineData("bot@agent.local")]
        public async Task Create_With_A_Name_That_Breaks_The_Rule_Should_Return_400_On_Name(string name)
        {
            var admin = await _portal.CreateAdminClientAsync();
            var usersBefore = await _portal.WithDbContextAsync(db => db.Users.CountAsync(x => x.Email != null && x.Email.EndsWith("@agent.local")));

            var response = await admin.PostAsync(AgentsUrl, new { Name = name }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal("Name", Assert.Single(error.Errors ?? new List<ErrorDetail>()).Property);

            Assert.Equal(usersBefore, await _portal.WithDbContextAsync(db => db.Users.CountAsync(x => x.Email != null && x.Email.EndsWith("@agent.local"))));
        }

        [Theory]
        [InlineData("a-1")]
        [InlineData("the-longest-name-an-agent-can-have-40-ch")]
        public async Task Create_Should_Accept_The_Shortest_And_The_Longest_Name(string name)
        {
            var created = await CreateAgentAsync(await _portal.CreateAdminClientAsync(), name);

            Assert.Equal($"{name}@agent.local", created.Email);
        }

        [Fact]
        public async Task Create_With_A_Name_That_Is_Taken_Should_Return_400_On_Name_And_Keep_The_First_Key()
        {
            var admin = await _portal.CreateAdminClientAsync();
            var first = await CreateAgentAsync(admin);
            var name = first.Email.Split('@')[0];

            var response = await admin.PostAsync(AgentsUrl, new { Name = name }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var detail = Assert.Single((await response.ReadJsonAsync<ErrorResponse>()).Errors ?? new List<ErrorDetail>());
            Assert.Equal("Name", detail.Property);
            Assert.Equal("An agent with this name already exists", detail.Message);

            Assert.Equal(1, await _portal.WithDbContextAsync(db => db.Users.CountAsync(x => x.Email == first.Email)));
            Assert.Equal(HttpStatusCode.OK, (await KeyClient(first.Key).GetAsync(SessionUrl)).StatusCode);
        }

        [Fact]
        public async Task Create_Should_Be_For_Administrators_Only()
        {
            var name = NewName();

            var user = await (await _portal.CreateUserClientAsync()).PostAsync(AgentsUrl, new { Name = name }.ToJsonContent());
            var anonymous = await _portal.CreateAnonymousClient().PostAsync(AgentsUrl, new { Name = name }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, user.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.False(await _portal.WithDbContextAsync(db => db.Users.AnyAsync(x => x.Email == name + "@agent.local")));
        }

        // ---------- Calling the API with the key ----------

        [Fact]
        public async Task Key_Should_Read_And_Write_A_Shared_Application_Without_A_Cookie_Or_The_Csrf_Header()
        {
            var scene = await CreateSceneAsync();
            await _portal.WithDbContextAsync(async db =>
            {
                var collaborator = await db.Collaborations.SingleAsync(x => x.AppID == scene.Shared.ID && x.UserEmail == scene.Created.Email);
                db.AgentPermissions.Add(new PortalAgentPermission
                {
                    Collaboration = collaborator,
                    PermissionsJson = JsonSerializer.Serialize(new[]
                    {
                        new AgentPermissionGrant { Resource = AgentPermissionResources.Application, Write = true }
                    })
                });
                return await db.SaveChangesAsync();
            });
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");

            // The request is the agent's.
            var session = await (await scene.Agent.GetAsync(SessionUrl)).ReadJsonAsync<SessionResponse>();
            Assert.Equal(scene.Created.Email, session.Email);
            Assert.False(session.IsAdmin);

            // A read.
            var read = await scene.Agent.GetAsync(scene.SharedUrl);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.False((await read.ReadJsonAsync<ApplicationResponse>()).IsOwner);

            var list = await (await scene.Agent.GetAsync(ApplicationsUrl)).ReadJsonAsync<ListResponse<ApplicationResponse>>();
            Assert.Equal(scene.Shared.Token, Assert.Single(list.Data).Token);

            // A write, with no X-Apilane-Portal header.
            var write = await scene.Agent.PutAsync($"{scene.SharedUrl}/status", new { Online = false }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, write.StatusCode);
            Assert.False((await write.ReadJsonAsync<ApplicationResponse>()).Online);
            Assert.False(await _portal.WithDbContextAsync(db => db.Applications.Where(x => x.ID == scene.Shared.ID).Select(x => x.Online).SingleAsync()));

            // Everything after the key check saw a normal user: the audit row is the agent's, and
            // the API server was called with the agent's own token.
            var audit = Assert.Single(await _portal.WithDbContextAsync(db => db.AuditLogs.AsNoTracking().Where(x => x.UserEmail == scene.Created.Email).ToListAsync()));
            Assert.Equal($"{scene.Created.ID} | Application | Modified", $"{audit.UserId} | {audit.EntityType} | {audit.Action}");

            var token = await _portal.WithDbContextAsync(db => db.Users.Where(x => x.Id == scene.Created.ID).Select(x => x.AdminAuthToken).SingleAsync());
            Assert.False(string.IsNullOrWhiteSpace(token));
            Assert.Equal($"Bearer {token}", Assert.Single(_portal.ApiServer.Requests).Headers["Authorization"]);
        }

        [Fact]
        public async Task Key_On_An_Application_That_Is_Not_Shared_Should_Answer_As_For_Any_User_Without_Access()
        {
            var scene = await CreateSceneAsync();
            var stranger = await _portal.CreateUserClientAsync();

            var agent = await scene.Agent.GetAsync(scene.NotSharedUrl);
            var user = await stranger.GetAsync(scene.NotSharedUrl);

            Assert.Equal(HttpStatusCode.NotFound, agent.StatusCode);
            Assert.Equal(user.StatusCode, agent.StatusCode);
            Assert.Equal(Describe(await user.ReadJsonAsync<ErrorResponse>()), Describe(await agent.ReadJsonAsync<ErrorResponse>()));

            var write = await scene.Agent.PutAsync($"{scene.NotSharedUrl}/status", new { Online = false }.ToJsonContent());
            Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Wrong_Unknown_And_Malformed_Keys_Should_All_Return_The_Same_401()
        {
            var scene = await CreateSceneAsync();
            var key = scene.Created.Key;
            var keyId = key.Split('_')[1];
            var secret = key.Split('_', 3)[2];

            var values = new[]
            {
                // A wrong secret, with and without the right length.
                $"Bearer {key.Substring(0, key.Length - 1)}{(key.EndsWith('A') ? 'B' : 'A')}",
                $"Bearer apl_{keyId}_wrong",
                // An unknown KeyId with the right secret.
                $"Bearer apl_000000000000_{secret}",
                // Not a key at all.
                "Bearer",
                "Bearer ",
                "Bearer not-a-key",
                "Bearer apl_",
                $"Bearer apl_{keyId}_",
                $"Bearer apl_{keyId}",
                $"Bearer xyz_{keyId}_{secret}",
                $"Bearer {key} {key}"
            };

            var answers = new List<string>();

            foreach (var value in values)
            {
                var client = _portal.CreateCookielessClient();
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", value);

                // On an endpoint that needs a session and on one that does not: a Bearer value is always checked.
                foreach (var url in new[] { scene.SharedUrl, "/api/v1/instance" })
                {
                    var response = await client.GetAsync(url);

                    Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"'{value}' on {url} answered {(int)response.StatusCode}.");
                    answers.Add(Describe(await response.ReadJsonAsync<ErrorResponse>()));
                }
            }

            Assert.Equal($"{PortalErrorCode.Unauthorized} | {PortalAgent.InvalidKeyMessage} |  | ", Assert.Single(answers.Distinct()));
        }

        [Fact]
        public async Task Bearer_Value_Should_Decide_Alone_Whatever_Cookie_Comes_With_It()
        {
            var scene = await CreateSceneAsync();
            var (adminEmail, adminPassword) = await _portal.CreateAdminUserAsync();
            var session = await _portal.SignInAsync(adminEmail, adminPassword);

            // A valid cookie does not save a wrong key.
            var wrong = new HttpRequestMessage(HttpMethod.Get, SessionUrl);
            wrong.Headers.Add("Cookie", session.Cookie);
            wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scene.Created.Key + "x");

            var wrongResponse = await _portal.CreateCookielessClient().SendAsync(wrong);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);
            Assert.Equal(PortalAgent.InvalidKeyMessage, (await wrongResponse.ReadJsonAsync<ErrorResponse>()).Message);

            // With a valid key the request is the agent's, not the administrator's.
            session.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scene.Created.Key);

            Assert.Equal(scene.Created.Email, (await (await session.Client.GetAsync(SessionUrl)).ReadJsonAsync<SessionResponse>()).Email);
            await AssertRefusedAsync(await session.Client.GetAsync(UsersUrl), "GET admin/users with an administrator's cookie");
        }

        // ---------- What an agent is refused ----------

        [Fact]
        public async Task Key_Should_Be_Refused_On_Deletes_Admin_Routes_And_The_Marked_Actions()
        {
            var scene = await CreateSceneAsync();
            var (personEmail, _) = await _portal.CreateUserAsync();
            var personId = await GetUserIdAsync(personEmail);
            var app = scene.SharedUrl;
            var registerEmail = $"user-{Guid.NewGuid():N}@portal.test";

            var calls = new (HttpMethod Method, string Url, object? Body)[]
            {
                // Destructive calls that cannot be granted.
                (HttpMethod.Delete, app, null),
                (HttpMethod.Delete, $"{app}/collaborators/1", null),
                (HttpMethod.Delete, SessionUrl, null),
                (HttpMethod.Put, $"{app}/collaborators/1/permissions", new { Permissions = Array.Empty<AgentPermissionGrant>() }),
                // The encryption key and new applications.
                (HttpMethod.Get, $"{app}/connection-info", null),
                (HttpMethod.Post, ApplicationsUrl, new { Name = "agent-app", ServerID = scene.Shared.ServerID, DatabaseType = "SQLLite" }),
                (HttpMethod.Post, $"{ApplicationsUrl}/import", new MultipartFormDataContent { { new StringContent("1"), "ServerID" } }),
                (HttpMethod.Post, $"{app}/clones", new { ServerID = scene.Shared.ServerID, DatabaseType = "SQLLite" }),
                // Everything under /api/v1/admin.
                (HttpMethod.Get, UsersUrl, null),
                (HttpMethod.Put, $"{UsersUrl}/{personId}/role", new { IsAdmin = true }),
                (HttpMethod.Put, $"{UsersUrl}/{scene.Created.ID}/role", new { IsAdmin = true }),
                (HttpMethod.Post, AgentsUrl, new { Name = NewName() }),
                (HttpMethod.Get, "/api/v1/admin/servers", null),
                (HttpMethod.Get, "/api/v1/admin/settings", null),
                (HttpMethod.Get, "/api/v1/admin/backup", null),
                (HttpMethod.Get, "/api/v1/admin/audit-log", null),
                (HttpMethod.Get, "/api/v1/admin/applications", null),
                // The data-plane token, and the account endpoints that sign in or set a password.
                (HttpMethod.Get, $"{SessionUrl}/api-token", null),
                (HttpMethod.Post, SessionUrl, new { Email = PortalFactory.AdminEmail, Password = PortalFactory.AdminPassword }),
                (HttpMethod.Post, "/api/v1/account", new { Email = registerEmail, Password = "a-password", ConfirmPassword = "a-password" }),
                (HttpMethod.Post, "/api/v1/account/password-resets", new { Email = personEmail, Code = "code", Password = "a-password", ConfirmPassword = "a-password" }),
                (HttpMethod.Put, "/api/v1/account/password", new { OldPassword = "old-password", NewPassword = "a-password", ConfirmPassword = "a-password" })
            };

            foreach (var call in calls)
            {
                var request = new HttpRequestMessage(call.Method, call.Url) { Content = call.Body as HttpContent ?? call.Body?.ToJsonContent() };

                await AssertRefusedAsync(await scene.Agent.SendAsync(request), $"{call.Method} {call.Url}");
            }

            // Nothing happened: the application is there, no API server was called, nobody was
            // registered or made an administrator, and the agent still works.
            Assert.True(await _portal.WithDbContextAsync(db => db.Applications.AnyAsync(x => x.ID == scene.Shared.ID)));
            Assert.Empty(_portal.ApiServer.Requests);
            Assert.False(await _portal.WithDbContextAsync(db => db.Users.AnyAsync(x => x.Email == registerEmail)));
            Assert.False(await IsAdminAsync(personId));
            Assert.False(await IsAdminAsync(scene.Created.ID));
            Assert.Equal(HttpStatusCode.OK, (await scene.Agent.GetAsync(scene.SharedUrl)).StatusCode);
        }

        [Fact]
        public async Task SetRole_Should_Refuse_To_Make_An_Agent_An_Administrator()
        {
            var admin = await _portal.CreateAdminClientAsync();
            var agent = await CreateAgentAsync(admin);

            var response = await admin.PutAsync($"{UsersUrl}/{agent.ID}/role", new UserRoleRequest { IsAdmin = true }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Conflict, error.Code);
            Assert.Equal("An agent cannot be an administrator", error.Message);
            Assert.False(await IsAdminAsync(agent.ID));

            // Asking for what it already is still succeeds, as for any user.
            var ordinary = await admin.PutAsync($"{UsersUrl}/{agent.ID}/role", new UserRoleRequest { IsAdmin = false }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, ordinary.StatusCode);
        }

        // ---------- Guards for people ----------

        [Theory]
        [InlineData("@agent.local")]
        [InlineData("@Agent.LOCAL")]
        public async Task Register_With_An_Agent_Address_Should_Return_400_On_Email(string suffix)
        {
            var email = $"person-{Guid.NewGuid():N}{suffix}";

            var response = await _portal.CreateAnonymousClient().PostAsync(
                "/api/v1/account",
                new { Email = email, Password = "a-password", ConfirmPassword = "a-password" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Email", Assert.Single((await response.ReadJsonAsync<ErrorResponse>()).Errors ?? new List<ErrorDetail>()).Property);
            Assert.False(await _portal.WithDbContextAsync(db => db.Users.AnyAsync(x => x.NormalizedEmail == email.ToUpperInvariant())));
        }

        [Fact]
        public async Task Forgot_Password_For_An_Agent_Should_Answer_202_And_Send_Nothing()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);

            var agent = await CreateAgentAsync(await _portal.CreateAdminClientAsync());
            var (personEmail, _) = await _portal.CreateUserAsync();
            var client = _portal.CreateAnonymousClient();

            var forAgent = await client.PostAsync("/api/v1/account/password-reset-requests", new { Email = agent.Email }.ToJsonContent());
            var forPerson = await client.PostAsync("/api/v1/account/password-reset-requests", new { Email = personEmail }.ToJsonContent());

            // The same answer as for an unknown address, and no mail; a person does get one.
            Assert.Equal(HttpStatusCode.Accepted, forAgent.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, forPerson.StatusCode);
            Assert.Empty(_portal.Mail.SentTo(agent.Email));
            Assert.Single(_portal.Mail.SentTo(personEmail));
        }

        // ---------- Sharing with an agent ----------

        [Fact]
        public async Task Share_With_An_Agent_Should_Send_No_Mail_While_A_Person_Still_Gets_One()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);

            var agent = await CreateAgentAsync(await _portal.CreateAdminClientAsync());
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var (personEmail, _) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();
            var application = (await _portal.CreateApplicationAsync(server.ID, ownerEmail, $"mail-{Guid.NewGuid():N}")).Application;
            var owner = await _portal.CreateSignedInClientAsync(ownerEmail, ownerPassword);
            var url = $"{ApplicationsUrl}/{application.Token}/collaborators";

            var forAgent = await owner.PostAsync(url, new { Email = agent.Email }.ToJsonContent());
            var forPerson = await owner.PostAsync(url, new { Email = personEmail }.ToJsonContent());

            // The agent is a collaborator like the person, and reaches the application; only the mail differs.
            Assert.Equal(HttpStatusCode.Created, forAgent.StatusCode);
            Assert.False((await forAgent.ReadJsonAsync<CollaboratorAddedResponse>()).NotificationSent);
            Assert.Empty(_portal.Mail.SentTo(agent.Email));
            Assert.Equal(HttpStatusCode.OK, (await KeyClient(agent.Key).GetAsync($"{ApplicationsUrl}/{application.Token}")).StatusCode);

            Assert.Equal(HttpStatusCode.Created, forPerson.StatusCode);
            Assert.True((await forPerson.ReadJsonAsync<CollaboratorAddedResponse>()).NotificationSent);
            Assert.Single(_portal.Mail.SentTo(personEmail));

            // The address marks an agent whatever its letter case, on another application too.
            var other = (await _portal.CreateApplicationAsync(server.ID, ownerEmail, $"mail-{Guid.NewGuid():N}")).Application;
            var upperCase = agent.Email.ToUpperInvariant();

            var forUpperCase = await owner.PostAsync($"{ApplicationsUrl}/{other.Token}/collaborators", new { Email = upperCase }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, forUpperCase.StatusCode);
            Assert.False((await forUpperCase.ReadJsonAsync<CollaboratorAddedResponse>()).NotificationSent);
            Assert.Empty(_portal.Mail.SentTo(upperCase));
        }

        [Fact]
        public async Task Available_Agents_Should_Leave_Out_The_Agents_That_Are_Already_Collaborators()
        {
            var admin = await _portal.CreateAdminClientAsync();
            var shared = await CreateAgentAsync(admin);
            var sharedInUpperCase = await CreateAgentAsync(admin);
            var free = await CreateAgentAsync(admin);
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var (collaboratorEmail, collaboratorPassword) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();

            var application = (await _portal.CreateApplicationAsync(
                server.ID, ownerEmail, $"agents-{Guid.NewGuid():N}", shared.Email, sharedInUpperCase.Email.ToUpperInvariant(), collaboratorEmail)).Application;

            var owner = await _portal.CreateSignedInClientAsync(ownerEmail, ownerPassword);
            var collaborators = $"{ApplicationsUrl}/{application.Token}/collaborators";
            var url = $"{collaborators}/available-agents";

            var response = await owner.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Name and address, nothing else: the strict reader fails on any extra property.
            // Other tests leave agents behind, so only the ones of this test are looked for.
            var list = await response.ReadJsonAsync<ListResponse<AvailableAgentResponse>>();
            var listed = Assert.Single(list.Data, x => x.Email == free.Email);
            Assert.Equal(free.Email.Split('@')[0], listed.Name);
            Assert.DoesNotContain(list.Data, x => x.Email == shared.Email || x.Email == sharedInUpperCase.Email);

            // Agents only, never a person, and in the order of their names.
            Assert.All(list.Data, x => Assert.Equal($"{x.Name}@agent.local", x.Email));
            Assert.Equal(list.Data.Select(x => x.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase), list.Data.Select(x => x.Name));

            // Once shared, the agent is no longer offered.
            Assert.Equal(HttpStatusCode.Created, (await owner.PostAsync(collaborators, new { Email = free.Email }.ToJsonContent())).StatusCode);

            var after = await (await owner.GetAsync(url)).ReadJsonAsync<ListResponse<AvailableAgentResponse>>();
            Assert.DoesNotContain(after.Data, x => x.Email == free.Email);

            // The rule of the other collaborator endpoints: for the owner only.
            var collaborator = await _portal.CreateSignedInClientAsync(collaboratorEmail, collaboratorPassword);

            Assert.Equal(HttpStatusCode.Forbidden, (await collaborator.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await KeyClient(shared.Key).GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await (await _portal.CreateUserClientAsync()).GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _portal.CreateAnonymousClient().GetAsync(url)).StatusCode);
        }

        // ---------- Delete ----------

        [Fact]
        public async Task Delete_Should_Stop_The_Key_And_Remove_The_Agent_From_The_Collaborator_Lists()
        {
            var admin = await _portal.CreateAdminClientAsync();
            var created = await CreateAgentAsync(admin);
            var (ownerEmail, _) = await _portal.CreateUserAsync();
            var (otherEmail, _) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();

            // Shared with the agent as its address is, and in another letter case.
            var first = await _portal.CreateApplicationAsync(server.ID, ownerEmail, $"first-{Guid.NewGuid():N}", created.Email, otherEmail);
            var second = await _portal.CreateApplicationAsync(server.ID, ownerEmail, $"second-{Guid.NewGuid():N}", created.Email.ToUpperInvariant());

            var agent = KeyClient(created.Key);
            Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync($"{ApplicationsUrl}/{first.Application.Token}")).StatusCode);

            var response = await admin.DeleteAsync($"{AgentsUrl}/{created.ID}");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            // The key is now an unknown one.
            var after = await agent.GetAsync($"{ApplicationsUrl}/{first.Application.Token}");
            Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
            Assert.Equal(PortalAgent.InvalidKeyMessage, (await after.ReadJsonAsync<ErrorResponse>()).Message);

            Assert.False(await _portal.WithDbContextAsync(db => db.Users.AnyAsync(x => x.Id == created.ID)));
            Assert.False(await _portal.WithDbContextAsync(db => db.AgentKeys.AnyAsync(x => x.UserId == created.ID)));

            // Its entries are gone from both applications; the other collaborator stays.
            var left = await _portal.WithDbContextAsync(db => db.Collaborations
                .AsNoTracking()
                .Where(x => x.AppID == first.Application.ID || x.AppID == second.Application.ID)
                .Select(x => x.UserEmail)
                .ToListAsync());
            Assert.Equal(new[] { otherEmail }, left);

            // So a new agent with the same name starts with nothing.
            var again = await CreateAgentAsync(admin, created.Email.Split('@')[0]);
            Assert.NotEqual(created.Key, again.Key);
            Assert.Empty((await (await KeyClient(again.Key).GetAsync(ApplicationsUrl)).ReadJsonAsync<ListResponse<ApplicationResponse>>()).Data);
        }

        [Fact]
        public async Task Delete_A_Person_Or_An_Unknown_Id_Should_Return_404_And_Delete_Nothing()
        {
            var admin = await _portal.CreateAdminClientAsync();
            var (personEmail, _) = await _portal.CreateUserAsync();
            var personId = await GetUserIdAsync(personEmail);

            var person = await admin.DeleteAsync($"{AgentsUrl}/{personId}");
            var unknown = await admin.DeleteAsync($"{AgentsUrl}/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.NotFound, person.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal("Agent", (await person.ReadJsonAsync<ErrorResponse>()).Entity);
            Assert.True(await _portal.WithDbContextAsync(db => db.Users.AnyAsync(x => x.Id == personId)));
        }

        // ---------- Upgrade ----------

        [Fact]
        public async Task EnsureSchemaUpdated_Should_Add_The_Agent_Keys_Table_To_A_Database_Made_Before_Agents()
        {
            var folder = Directory.CreateTempSubdirectory("apilane-portal-upgrade-").FullName;

            try
            {
                // No pooling, so the file is free to delete when the context is gone.
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite($"Data Source={Path.Combine(folder, "Apilane.db")};Pooling=False")
                    .UseApplicationServiceProvider(_portal.Services)
                    .Options;

                await using var db = new ApplicationDbContext(options, _portal.Services.GetRequiredService<ILogger<ApplicationDbContext>>());

                // The database as the version before agents made it: everything but the new table.
                await db.Database.EnsureCreatedAsync();
                await db.Database.ExecuteSqlRawAsync("DROP TABLE \"AgentKeys\"");
                Assert.False(await HasAgentKeysTableAsync(db));

                // What Program.cs runs at every start; a second run changes nothing.
                db.EnsureSchemaUpdated();
                db.EnsureSchemaUpdated();

                Assert.True(await HasAgentKeysTableAsync(db));

                // The table is the one of a new installation: it takes a key, ...
                var agent = new ApplicationUser { UserName = "upgrade@agent.local", Email = "upgrade@agent.local" };
                var other = new ApplicationUser { UserName = "other@agent.local", Email = "other@agent.local" };
                db.Users.AddRange(agent, other);
                db.AgentKeys.Add(new PortalAgentKey { UserId = agent.Id, KeyId = "0123456789ab", SecretHash = "hash" });
                await db.SaveChangesAsync();

                // ... refuses a second key with the same KeyId, ...
                db.AgentKeys.Add(new PortalAgentKey { UserId = other.Id, KeyId = "0123456789ab", SecretHash = "other-hash" });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                db.ChangeTracker.Clear();

                // ... and loses the key when its user is deleted.
                await db.Users.Where(x => x.Id == agent.Id).ExecuteDeleteAsync();
                Assert.False(await db.AgentKeys.AnyAsync());
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        // ---------- Helpers ----------

        private record Scene(AgentCreatedResponse Created, HttpClient Agent, AppInfo Shared, AppInfo NotShared)
        {
            public string SharedUrl => $"{ApplicationsUrl}/{Shared.Token}";

            public string NotSharedUrl => $"{ApplicationsUrl}/{NotShared.Token}";
        }

        private record AppInfo(long ID, string Token, long ServerID);

        /// <summary>
        /// A new agent with a client that sends only its key, and two applications of another
        /// user: one shared with the agent's address, one not.
        /// </summary>
        private async Task<Scene> CreateSceneAsync()
        {
            var created = await CreateAgentAsync(await _portal.CreateAdminClientAsync());
            var (ownerEmail, _) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();

            var shared = (await _portal.CreateApplicationAsync(server.ID, ownerEmail, $"shared-{Guid.NewGuid():N}", created.Email)).Application;
            var notShared = (await _portal.CreateApplicationAsync(server.ID, ownerEmail, $"not-shared-{Guid.NewGuid():N}")).Application;

            return new Scene(
                created,
                KeyClient(created.Key),
                new AppInfo(shared.ID, shared.Token, server.ID),
                new AppInfo(notShared.ID, notShared.Token, server.ID));
        }

        private async Task<AgentCreatedResponse> CreateAgentAsync(HttpClient admin, string? name = null)
        {
            var response = await admin.PostAsync(AgentsUrl, new { Name = name ?? NewName() }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            return await response.ReadJsonAsync<AgentCreatedResponse>();
        }

        /// <summary>
        /// A client as a script is: no cookies, no X-Apilane-Portal header, only the key.
        /// </summary>
        private HttpClient KeyClient(string key)
        {
            var client = _portal.CreateCookielessClient();

            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

            return client;
        }

        private static string NewName()
        {
            return $"bot-{Guid.NewGuid():N}".Substring(0, 24);
        }

        private static string Describe(ErrorResponse error)
        {
            return $"{error.Code} | {error.Message} | {error.Entity} | {error.Property}";
        }

        private static async Task AssertRefusedAsync(HttpResponseMessage response, string call)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{call} answered {(int)response.StatusCode}, not 403.");

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.True(error.Message == PortalAgent.RefusedMessage, $"{call} answered '{error.Message}'.");
        }

        private Task<string> GetUserIdAsync(string email)
        {
            return _portal.WithDbContextAsync(db => db.Users.AsNoTracking().Where(x => x.Email == email).Select(x => x.Id).SingleAsync());
        }

        private Task<bool> IsAdminAsync(string userId)
        {
            return _portal.WithDbContextAsync(async db =>
            {
                var role = await db.Roles.AsNoTracking().SingleAsync(x => x.Name == Globals.AdminRoleName);

                return await db.UserRoles.AnyAsync(x => x.UserId == userId && x.RoleId == role.Id);
            });
        }

        private static async Task<bool> HasAgentKeysTableAsync(ApplicationDbContext db)
        {
            var count = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = 'AgentKeys'")
                .SingleAsync();

            return count == 1;
        }
    }
}
