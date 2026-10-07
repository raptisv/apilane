using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class BootstrapApiTests
    {
        private const string SetupUrl = "/api/v1/bootstrap";
        private const string ChosenEmail = "Chosen.Admin@portal.test";
        private const string ChosenPassword = "a-new-administrator-password";

        [Fact]
        public async Task Fresh_Instance_Should_Require_Setup_And_Replace_The_Random_Credential_On_Restart()
        {
            using var portal = new PortalFactory(completeBootstrap: false);
            var client = portal.CreateAnonymousClient();
            var status = await (await client.GetAsync(SetupUrl)).ReadJsonAsync<BootstrapResponse>();
            Assert.True(status.Required);

            var credential = await IssueCredentialAsync(portal);
            var user = await BootstrapUserAsync(portal);
            Assert.Null(user.Email);
            Assert.Null(user.NormalizedEmail);
            Assert.StartsWith("bootstrap-", user.UserName);
            Assert.Equal(user.UserName?.ToUpperInvariant(), user.NormalizedUserName);
            Assert.Equal(64, credential.TemporaryPassword.Length);
            Assert.DoesNotContain(credential.TemporaryPassword, await client.GetStringAsync(SetupUrl));
            Assert.False(await PasswordWorksAsync(portal, "admin"));
            Assert.True(await PasswordWorksAsync(portal, credential.TemporaryPassword));

            var restarted = portal.CreateHost(_ => { });
            Assert.True((await (await portal.CreateAnonymousClient(restarted).GetAsync(SetupUrl)).ReadJsonAsync<BootstrapResponse>()).Required);
            Assert.False(await PasswordWorksAsync(portal, credential.TemporaryPassword));
            Assert.False(await PasswordWorksAsync(portal, "admin"));
        }

        [Fact]
        public async Task Pending_Setup_Should_Block_Management_Registration_And_Internal_Access()
        {
            using var portal = new PortalFactory(completeBootstrap: false);
            var credential = await IssueCredentialAsync(portal);
            var client = portal.CreateAnonymousClient();
            client.DefaultRequestHeaders.Add("x-installation-key", await portal.StoredInstallationKeyAsync());

            var requests = new[]
            {
                (HttpMethod.Get, "/api/v1/session"),
                (HttpMethod.Get, "/api/v1/session/api-token"),
                (HttpMethod.Post, "/api/v1/session"),
                (HttpMethod.Post, "/api/v1/account"),
                (HttpMethod.Post, "/api/v1/account/password-reset-requests"),
                (HttpMethod.Get, "/api/v1/applications"),
                (HttpMethod.Get, "/api/v1/admin/backup"),
                (HttpMethod.Get, "/api/internal/applications/00000000-0000-0000-0000-000000000000"),
                (HttpMethod.Get, "/swagger/index.html")
            };

            foreach (var (method, path) in requests)
            {
                using var request = new HttpRequestMessage(method, path)
                {
                    Content = new { Email = ChosenEmail, Password = credential.TemporaryPassword }.ToJsonContent()
                };
                var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal("SETUP_REQUIRED", (await response.ReadJsonAsync<ErrorResponse>()).Code);
                Assert.False(response.Headers.Contains("Set-Cookie"));
            }

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/instance")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/liveness")).StatusCode);
        }

        [Fact]
        public async Task Complete_Should_Require_The_Operator_Credential_And_A_Valid_Different_Password()
        {
            using var portal = new PortalFactory(completeBootstrap: false);
            var credential = await IssueCredentialAsync(portal);
            var client = portal.CreateAnonymousClient();
            var before = await SnapshotAsync(portal);

            var wrongCredential = await client.PostAsync(SetupUrl, Request(credential, temporaryPassword: "admin").ToJsonContent());
            Assert.Equal(HttpStatusCode.Unauthorized, wrongCredential.StatusCode);
            Assert.False(wrongCredential.Headers.Contains("Set-Cookie"));

            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsync(SetupUrl, Request(credential, password: "short").ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsync(SetupUrl, Request(credential, password: credential.TemporaryPassword).ToJsonContent())).StatusCode);

            var mismatch = Request(credential);
            mismatch.ConfirmPassword = "a-different-password";
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(SetupUrl, mismatch.ToJsonContent())).StatusCode);

            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(SetupUrl, Request(credential).ToJsonContent())).StatusCode);
            Assert.True((await (await client.GetAsync(SetupUrl)).ReadJsonAsync<BootstrapResponse>()).Required);
            Assert.Equal(before, await SnapshotAsync(portal));
            Assert.True(await PasswordWorksAsync(portal, credential.TemporaryPassword));
        }

        [Fact]
        public async Task Complete_Should_Save_The_Chosen_Email_Replace_The_Password_Sign_In_And_Reject_Replay()
        {
            using var portal = new PortalFactory(completeBootstrap: false);
            var credential = await IssueCredentialAsync(portal);
            var client = portal.CreateAnonymousClient();

            var response = await client.PostAsync(SetupUrl, Request(credential, email: "  " + ChosenEmail + "  ").ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.SetsPersistentCookie());
            var session = await response.ReadJsonAsync<SessionResponse>();
            Assert.Equal(ChosenEmail, session.Email);
            Assert.True(session.IsAdmin);
            var user = await BootstrapUserAsync(portal);
            Assert.Equal(ChosenEmail, user.Email);
            Assert.Equal(ChosenEmail, user.UserName);
            Assert.Equal(ChosenEmail.ToUpperInvariant(), user.NormalizedEmail);
            Assert.Equal(ChosenEmail.ToUpperInvariant(), user.NormalizedUserName);
            Assert.True(user.EmailConfirmed);
            Assert.False((await (await client.GetAsync(SetupUrl)).ReadJsonAsync<BootstrapResponse>()).Required);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/session/api-token")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admin/users")).StatusCode);
            Assert.False(await PasswordWorksAsync(portal, credential.TemporaryPassword));
            Assert.True(await PasswordWorksAsync(portal, ChosenPassword));

            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(SetupUrl, Request(credential).ToJsonContent())).StatusCode);
            await portal.SignInAsync(ChosenEmail.ToLowerInvariant(), ChosenPassword);
            var restarted = portal.CreateHost(_ => { });
            Assert.False((await (await portal.CreateAnonymousClient(restarted).GetAsync(SetupUrl)).ReadJsonAsync<BootstrapResponse>()).Required);
            Assert.True(await PasswordWorksAsync(portal, ChosenPassword));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-an-email")]
        [InlineData("operator@agent.local")]
        [InlineData("  Operator@AGENT.LOCAL  ")]
        public async Task Complete_With_An_Invalid_Or_Reserved_Email_Should_Leave_Setup_Unchanged(string email)
        {
            using var portal = new PortalFactory(completeBootstrap: false);
            var credential = await IssueCredentialAsync(portal);
            var before = await SnapshotAsync(portal);
            var client = portal.CreateAnonymousClient();

            var response = await client.PostAsync(SetupUrl, Request(credential, email: email).ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Contains(error.Errors ?? [], x => x.Property == nameof(BootstrapRequest.Email));
            Assert.Equal(before, await SnapshotAsync(portal));
            Assert.True(await PasswordWorksAsync(portal, credential.TemporaryPassword));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Complete_With_A_Duplicate_Email_Or_Username_Should_Leave_Setup_Unchanged(bool duplicateEmail)
        {
            using var portal = new PortalFactory(completeBootstrap: false);
            var credential = await IssueCredentialAsync(portal);

            using (var scope = portal.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var result = await users.CreateAsync(new ApplicationUser
                {
                    Email = duplicateEmail ? ChosenEmail.ToLowerInvariant() : "another-address@portal.test",
                    UserName = duplicateEmail ? "another-username@portal.test" : ChosenEmail.ToLowerInvariant(),
                    EmailConfirmed = true,
                    AdminAuthToken = "existing-user-token"
                }, "existing-user-password");
                Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));
            }

            var before = await SnapshotAsync(portal);
            var response = await portal.CreateAnonymousClient().PostAsync(SetupUrl, Request(credential).ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Contains(error.Errors ?? [], x => x.Property == nameof(BootstrapRequest.Email));
            Assert.Equal(before, await SnapshotAsync(portal));
            Assert.True(await PasswordWorksAsync(portal, credential.TemporaryPassword));
        }

        [Fact]
        public async Task Complete_When_Identity_Rejects_The_Password_Should_Roll_Back_The_Email_And_Setup_State()
        {
            using var portal = new PortalFactory(completeBootstrap: false);
            var host = portal.CreateHost(services => services.Configure<IdentityOptions>(options => options.Password.RequireDigit = true));
            var credential = await IssueCredentialAsync(portal);
            var before = await SnapshotAsync(portal);
            var client = portal.CreateAnonymousClient(host);

            var response = await client.PostAsync(SetupUrl, Request(credential).ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Contains(error.Errors ?? [], x => x.Property == nameof(BootstrapRequest.Password));
            Assert.Equal(before, await SnapshotAsync(portal));
            Assert.True(await PasswordWorksAsync(portal, credential.TemporaryPassword));
            Assert.False(await PasswordWorksAsync(portal, ChosenPassword));

            Assert.Equal(HttpStatusCode.OK,
                (await client.PostAsync(SetupUrl, Request(credential, password: "valid-password-with-digit-1").ToJsonContent())).StatusCode);
        }

        [Fact]
        public async Task Complete_When_Saving_The_Setup_State_Fails_Should_Roll_Back_The_Email_And_Password()
        {
            using var portal = new PortalFactory(completeBootstrap: false);
            var credential = await IssueCredentialAsync(portal);
            var before = await SnapshotAsync(portal);
            var client = portal.CreateAnonymousClient();
            await portal.WithDbContextAsync(db => db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER RejectBootstrapCompletion BEFORE UPDATE OF Completed ON BootstrapState "
                + "WHEN NEW.Completed = 1 BEGIN SELECT RAISE(ABORT, 'test setup state failure'); END"));

            try
            {
                var response = await client.PostAsync(SetupUrl, Request(credential).ToJsonContent());

                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.False(response.Headers.Contains("Set-Cookie"));
                Assert.Equal(before, await SnapshotAsync(portal));
                Assert.True(await PasswordWorksAsync(portal, credential.TemporaryPassword));
                Assert.False(await PasswordWorksAsync(portal, ChosenPassword));
                Assert.True((await (await client.GetAsync(SetupUrl)).ReadJsonAsync<BootstrapResponse>()).Required);
            }
            finally
            {
                await portal.WithDbContextAsync(db => db.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectBootstrapCompletion"));
            }

            var completed = await client.PostAsync(SetupUrl, Request(credential).ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            Assert.True(completed.Headers.Contains("Set-Cookie"));
            Assert.Equal(ChosenEmail, (await BootstrapUserAsync(portal)).Email);
            Assert.True(await PasswordWorksAsync(portal, ChosenPassword));
        }

        [Theory]
        [InlineData(PortalFactory.AdminPassword, true)]
        [InlineData("admin", true)]
        [InlineData(PortalFactory.AdminPassword, false)]
        [InlineData("admin", false)]
        public async Task Initialize_With_No_Setup_State_Should_Preserve_All_Existing_User_Fields(string password, bool hasAdministrator)
        {
            using var portal = new PortalFactory();
            await portal.CreateUserAsync();

            using (var scope = portal.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await db.Users.SingleAsync(x => x.Email == PortalFactory.AdminEmail);
                user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, password);
                user.AdminAuthToken = "previous-session-token";
                user.PhoneNumber = "+302100000000";
                user.PhoneNumberConfirmed = true;
                user.TwoFactorEnabled = true;
                user.AccessFailedCount = 2;
                user.LockoutEnabled = true;
                user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10);
                user.DateRegistered = DateTime.UtcNow.AddYears(-2);
                user.LastLogin = DateTime.UtcNow.AddDays(-1);
                db.UserTokens.Add(new IdentityUserToken<string>
                {
                    UserId = user.Id,
                    LoginProvider = "existing-provider",
                    Name = "existing-token",
                    Value = "existing-token-value"
                });

                if (!hasAdministrator)
                {
                    db.UserRoles.RemoveRange(await db.UserRoles.ToListAsync());
                }

                db.BootstrapStates.Remove(await db.BootstrapStates.SingleAsync());
                await db.SaveChangesAsync();
            }

            var before = await SnapshotAsync(portal, includeBootstrapState: false);
            using (var scope = portal.Services.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IPortalBootstrapService>();
                Assert.Null(await service.InitializeAsync());
                Assert.False(await service.IsRequiredAsync());

                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var state = await db.BootstrapStates.AsNoTracking().SingleAsync();
                Assert.True(state.Completed);
                Assert.Null(state.UserId);
                Assert.False(state.TemporaryPasswordIssued);
            }

            Assert.Equal(before, await SnapshotAsync(portal, includeBootstrapState: false));
            var restarted = portal.CreateHost(_ => { });
            Assert.False((await (await portal.CreateAnonymousClient(restarted).GetAsync(SetupUrl)).ReadJsonAsync<BootstrapResponse>()).Required);
            Assert.Equal(before, await SnapshotAsync(portal, includeBootstrapState: false));
        }

        [Fact]
        public async Task Complete_Should_Use_The_Account_Rate_Limit()
        {
            using var portal = new PortalFactory(completeBootstrap: false);
            var limited = portal.CreateHost(services => services.Configure<PortalRateLimitOptions>(options => options.PermitLimit = 1));
            var credential = await IssueCredentialAsync(portal);
            var client = portal.CreateAnonymousClient(limited);

            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsync(SetupUrl, Request(credential, temporaryPassword: "wrong").ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests,
                (await client.PostAsync(SetupUrl, Request(credential).ToJsonContent())).StatusCode);
        }

        [Fact]
        public async Task Concurrent_Completions_Should_Only_Set_One_Password_And_Issue_One_Session()
        {
            using var portal = new PortalFactory(completeBootstrap: false);
            var credential = await IssueCredentialAsync(portal);
            var first = portal.CreateAnonymousClient();
            var second = portal.CreateAnonymousClient();
            const string secondEmail = "Second.Admin@portal.test";
            const string secondPassword = "a-second-administrator-password";
            var responses = await Task.WhenAll(
                first.PostAsync(SetupUrl, Request(credential).ToJsonContent()),
                second.PostAsync(SetupUrl, Request(credential, password: secondPassword, email: secondEmail).ToJsonContent()));

            var successful = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            Assert.Single(responses, x => x.Headers.Contains("Set-Cookie"));
            var session = await successful.ReadJsonAsync<SessionResponse>();
            var user = await BootstrapUserAsync(portal);
            Assert.Equal(session.Email, user.Email);
            Assert.Equal(session.Email, user.UserName);
            Assert.True(await PasswordWorksAsync(portal, session.Email == ChosenEmail ? ChosenPassword : secondPassword));
            Assert.False(await PasswordWorksAsync(portal, session.Email == ChosenEmail ? secondPassword : ChosenPassword));
            Assert.False((await (await first.GetAsync(SetupUrl)).ReadJsonAsync<BootstrapResponse>()).Required);
            Assert.False(await PasswordWorksAsync(portal, credential.TemporaryPassword));
        }

        private static BootstrapRequest Request(BootstrapCredential credential, string password = ChosenPassword, string? temporaryPassword = null, string email = ChosenEmail)
        {
            return new BootstrapRequest
            {
                Email = email,
                TemporaryPassword = temporaryPassword ?? credential.TemporaryPassword,
                Password = password,
                ConfirmPassword = password
            };
        }

        private static async Task<BootstrapCredential> IssueCredentialAsync(PortalFactory portal)
        {
            using var scope = portal.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.BootstrapStates.SingleAsync()).TemporaryPasswordIssued = false;
            await db.SaveChangesAsync();
            return await scope.ServiceProvider.GetRequiredService<IPortalBootstrapService>().InitializeAsync()
                ?? throw new InvalidOperationException("A fresh setup credential was expected.");
        }

        private static async Task<bool> PasswordWorksAsync(PortalFactory portal, string password)
        {
            using var scope = portal.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var state = await db.BootstrapStates.AsNoTracking().SingleAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(state.UserId ?? string.Empty)
                ?? throw new InvalidOperationException("The seeded administrator is missing.");
            return await users.CheckPasswordAsync(user, password);
        }

        private static async Task<ApplicationUser> BootstrapUserAsync(PortalFactory portal)
        {
            using var scope = portal.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var state = await db.BootstrapStates.AsNoTracking().SingleAsync();
            return await db.Users.AsNoTracking().SingleAsync(x => x.Id == state.UserId);
        }

        private static async Task<string> SnapshotAsync(PortalFactory portal, bool includeBootstrapState = true)
        {
            using var scope = portal.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return JsonSerializer.Serialize(new
            {
                Users = await db.Users.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                UserRoles = await db.UserRoles.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.RoleId).ToListAsync(),
                UserTokens = await db.UserTokens.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.LoginProvider).ThenBy(x => x.Name).ToListAsync(),
                BootstrapState = includeBootstrapState ? await db.BootstrapStates.AsNoTracking().SingleAsync() : null
            });
        }
    }
}
