using Apilane.Common;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
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
    public class AdminUsersApiTests
    {
        private const string Url = "/api/v1/admin/users";

        private readonly PortalFactory _portal;

        public AdminUsersApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- List ----------

        [Fact]
        public async Task List_Should_Return_Users_With_Their_Role_And_Mark_The_Caller()
        {
            var (userEmail, _) = await _portal.CreateUserAsync();
            var (otherAdminEmail, _) = await _portal.CreateAdminUserAsync();
            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // The strict reader fails on any property outside the contract, such as a password hash.
            var users = await response.ReadJsonAsync<ListResponse<UserResponse>>();
            Assert.Equal(users.Data.Count, users.Total);

            var user = Assert.Single(users.Data, x => x.Email == userEmail);
            Assert.Equal(await GetUserIdAsync(userEmail), user.ID);
            Assert.False(user.IsAdmin);
            Assert.False(user.IsCurrentUser);

            var otherAdmin = Assert.Single(users.Data, x => x.Email == otherAdminEmail);
            Assert.True(otherAdmin.IsAdmin);
            Assert.False(otherAdmin.IsCurrentUser);

            var caller = Assert.Single(users.Data, x => x.Email == email);
            Assert.True(caller.IsAdmin);
            Assert.True(caller.IsCurrentUser);
            Assert.Equal(DateTimeKind.Utc, caller.LastLogin.Kind);
            Assert.True(caller.LastLogin > DateTime.UtcNow.AddMinutes(-5));
        }

        [Fact]
        public async Task List_Should_Be_Ordered_By_Last_Login_Newest_First()
        {
            var (olderEmail, _) = await _portal.CreateUserAsync();
            var (newerEmail, _) = await _portal.CreateUserAsync();
            await SetLastLoginAsync(olderEmail, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            await SetLastLoginAsync(newerEmail, new DateTime(2002, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var client = await _portal.CreateAdminClientAsync();

            var users = (await (await client.GetAsync(Url)).ReadJsonAsync<ListResponse<UserResponse>>()).Data;

            var newerIndex = users.FindIndex(x => x.Email == newerEmail);
            var olderIndex = users.FindIndex(x => x.Email == olderEmail);

            Assert.True(newerIndex >= 0 && newerIndex < olderIndex, "Users are not listed by last login, newest first.");
            Assert.Equal(new DateTime(2002, 1, 1, 0, 0, 0, DateTimeKind.Utc), users[newerIndex].LastLogin);
        }

        [Fact]
        public async Task List_Should_Send_Last_Login_As_Utc()
        {
            var client = await _portal.CreateAdminClientAsync();

            var body = await (await client.GetAsync(Url)).Content.ReadAsStringAsync();

            // Without the Z a browser would read the value as local time.
            Assert.Matches("\"LastLogin\":\"[0-9T:.-]+Z\"", body);
        }

        [Fact]
        public async Task List_Anonymous_Should_Return_401()
        {
            var response = await _portal.CreateAnonymousClient().GetAsync(Url);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task List_User_Without_Admin_Role_Should_Return_403()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync(Url);

            await AssertNotAdminAsync(response);
        }

        // ---------- Set role ----------

        [Fact]
        public async Task SetRole_Admin_Should_Give_The_Role_And_Add_An_Audit_Row_With_The_Email()
        {
            var (targetEmail, targetPassword) = await _portal.CreateUserAsync();
            var targetId = await GetUserIdAsync(targetEmail);
            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var response = await client.PutAsync(RoleUrl(targetId), Role(true));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var user = await response.ReadJsonAsync<UserResponse>();
            Assert.Equal(targetId, user.ID);
            Assert.Equal(targetEmail, user.Email);
            Assert.True(user.IsAdmin);
            Assert.False(user.IsCurrentUser);

            Assert.True(await IsAdminAsync(targetId));

            var audit = Assert.Single(await FindAuditAsync(targetEmail));
            Assert.Equal("Created", audit.Action);
            Assert.Equal(email, audit.UserEmail);
            Assert.Null(audit.AppID);

            // The role works: the user can now call an admin endpoint.
            var targetClient = await _portal.CreateSignedInClientAsync(targetEmail, targetPassword);
            Assert.Equal(HttpStatusCode.OK, (await targetClient.GetAsync(Url)).StatusCode);
        }

        [Fact]
        public async Task SetRole_Admin_Twice_Should_Succeed_And_Change_Nothing_The_Second_Time()
        {
            var (targetEmail, _) = await _portal.CreateUserAsync();
            var targetId = await GetUserIdAsync(targetEmail);
            var client = await _portal.CreateAdminClientAsync();

            var first = await client.PutAsync(RoleUrl(targetId), Role(true));
            var second = await client.PutAsync(RoleUrl(targetId), Role(true));

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.True((await second.ReadJsonAsync<UserResponse>()).IsAdmin);
            Assert.True(await IsAdminAsync(targetId));
            Assert.Single(await FindAuditAsync(targetEmail));
        }

        [Fact]
        public async Task SetRole_User_Should_Take_The_Role_Away_And_Add_An_Audit_Row_With_The_Email()
        {
            var (targetEmail, targetPassword) = await _portal.CreateAdminUserAsync();
            var targetId = await GetUserIdAsync(targetEmail);
            var targetClient = await _portal.CreateSignedInClientAsync(targetEmail, targetPassword);
            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var response = await client.PutAsync(RoleUrl(targetId), Role(false));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False((await response.ReadJsonAsync<UserResponse>()).IsAdmin);
            Assert.False(await IsAdminAsync(targetId));

            var audit = Assert.Single(await FindAuditAsync(targetEmail));
            Assert.Equal("Deleted", audit.Action);
            Assert.Equal(email, audit.UserEmail);

            // A session that was already open keeps the role until its cookie is refreshed, like the Razor page.
            // Checked before the new sign-in, which ends the older session.
            Assert.Equal(HttpStatusCode.OK, (await targetClient.GetAsync(Url)).StatusCode);

            // A new sign-in no longer has the role.
            var newTargetClient = await _portal.CreateSignedInClientAsync(targetEmail, targetPassword);
            await AssertNotAdminAsync(await newTargetClient.GetAsync(Url));
        }

        [Fact]
        public async Task SetRole_User_For_A_User_Without_The_Role_Should_Succeed_And_Change_Nothing()
        {
            var (targetEmail, _) = await _portal.CreateUserAsync();
            var targetId = await GetUserIdAsync(targetEmail);
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PutAsync(RoleUrl(targetId), Role(false));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False((await response.ReadJsonAsync<UserResponse>()).IsAdmin);
            Assert.False(await IsAdminAsync(targetId));
            Assert.Empty(await FindAuditAsync(targetEmail));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SetRole_Own_User_Should_Return_409_And_Keep_The_Role(bool isAdmin)
        {
            var (email, password) = await _portal.CreateAdminUserAsync();
            var ownId = await GetUserIdAsync(email);
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var response = await client.PutAsync(RoleUrl(ownId), Role(isAdmin));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Conflict, error.Code);
            Assert.Equal("Cannot change your own role", error.Message);
            Assert.Equal("User", error.Entity);

            Assert.True(await IsAdminAsync(ownId));
            Assert.Empty(await FindAuditAsync(email));
        }

        [Fact]
        public async Task SetRole_Unknown_User_Should_Return_404()
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PutAsync(RoleUrl(Guid.NewGuid().ToString()), Role(true));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.NotFound, error.Code);
            Assert.Equal("User", error.Entity);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"IsAdmin\":null}")]
        public async Task SetRole_Without_A_Value_Should_Return_400_And_Change_Nothing(string body)
        {
            var (targetEmail, _) = await _portal.CreateUserAsync();
            var targetId = await GetUserIdAsync(targetEmail);
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PutAsync(RoleUrl(targetId), new StringContent(body, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);

            var detail = Assert.Single(error.Errors ?? new List<ErrorDetail>());
            Assert.Equal("IsAdmin", detail.Property);
            Assert.Equal("Required", detail.Message);

            Assert.False(await IsAdminAsync(targetId));
        }

        [Fact]
        public async Task SetRole_Without_The_Csrf_Header_Should_Return_403_And_Change_Nothing()
        {
            var (targetEmail, _) = await _portal.CreateUserAsync();
            var targetId = await GetUserIdAsync(targetEmail);
            var client = await _portal.CreateAdminClientAsync();
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await client.PutAsync(RoleUrl(targetId), Role(true));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);

            Assert.False(await IsAdminAsync(targetId));
        }

        [Fact]
        public async Task SetRole_Anonymous_Should_Return_401_And_Change_Nothing()
        {
            var (targetEmail, _) = await _portal.CreateUserAsync();
            var targetId = await GetUserIdAsync(targetEmail);

            var response = await _portal.CreateAnonymousClient().PutAsync(RoleUrl(targetId), Role(true));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.False(await IsAdminAsync(targetId));
        }

        [Fact]
        public async Task SetRole_User_Without_Admin_Role_Should_Return_403_And_Change_Nothing()
        {
            var (targetEmail, _) = await _portal.CreateUserAsync();
            var targetId = await GetUserIdAsync(targetEmail);
            var client = await _portal.CreateUserClientAsync();

            var response = await client.PutAsync(RoleUrl(targetId), Role(true));

            await AssertNotAdminAsync(response);
            Assert.False(await IsAdminAsync(targetId));
        }

        // ---------- Helpers ----------

        private static string RoleUrl(string userId)
        {
            return $"{Url}/{userId}/role";
        }

        private static HttpContent Role(bool isAdmin)
        {
            return new UserRoleRequest { IsAdmin = isAdmin }.ToJsonContent();
        }

        private Task<string> GetUserIdAsync(string email)
        {
            return _portal.WithDbContextAsync(db => db.Users.AsNoTracking().Where(x => x.Email == email).Select(x => x.Id).SingleAsync());
        }

        private Task<int> SetLastLoginAsync(string email, DateTime lastLogin)
        {
            return _portal.WithDbContextAsync(async db =>
            {
                var user = await db.Users.SingleAsync(x => x.Email == email);
                user.LastLogin = lastLogin;

                return await db.SaveChangesAsync();
            });
        }

        private Task<bool> IsAdminAsync(string userId)
        {
            return _portal.WithDbContextAsync(async db =>
            {
                var role = await db.Roles.AsNoTracking().SingleAsync(x => x.Name == Globals.AdminRoleName);

                return await db.UserRoles.AnyAsync(x => x.UserId == userId && x.RoleId == role.Id);
            });
        }

        // The audit rows of role changes of one user: their identifier is the user's e-mail.
        private Task<List<PortalAuditLog>> FindAuditAsync(string targetEmail)
        {
            return _portal.WithDbContextAsync(db => db.AuditLogs
                .AsNoTracking()
                .Where(x => x.EntityType == "User Role" && x.EntityIdentifier == targetEmail)
                .ToListAsync());
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
