using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using Apilane.Net.Utilities;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// The API caches the user behind an authentication token. When the token's row disappears without
    /// the cache being told (the user is deleted, the owner removes it with SQL, the application is
    /// rebuilt), the cache must stop authenticating once it next checks the database, and must not
    /// affect tokens that still exist.
    /// </summary>
    public class AuthTokenRevocationTests : AppicationTestsBase
    {
        public AuthTokenRevocationTests() : base(SuiteContext.Shared)
        {
        }

        private const string Password = "password";
        private const string EntityName = "Notes";

        private class Note : DataItem
        {
            public string? Text { get; set; }
        }

        private async Task<(long UserId, long KeyId, string Token)> RegisterAndLoginAsync(string email)
        {
            var userId = (await ApilaneService.AccountRegisterAsync(
                AccountRegisterRequest.New(new RegisterItem { Email = email, Username = email, Password = Password })))
                .Match(r => r, e => throw new Exception($"Register failed | {email} | {e.Code} | {e.Message}"));

            var login = (await ApilaneService.AccountLoginAsync<ApiUser>(
                AccountLoginRequest.New(new LoginItem { Email = email, Password = Password })))
                .Match(r => r, e => throw new Exception($"Login failed | {email} | {e.Code} | {e.Message}"));

            return (userId, login.AuthTokenID, login.AuthToken);
        }

        // The probe is a read that only authenticated users may do. It depends on nothing but the token
        // being accepted (an account endpoint would also fail when the user row itself is gone).
        private Task<Either<DataResponse<Note>, ApilaneError>> ReadAsAsync(string token)
        {
            return ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(EntityName).WithAuthToken(token));
        }

        // The same read as a signed request: the token is the signing secret and is not sent
        private Task<Either<DataResponse<Note>, ApilaneError>> ReadSignedAsync(long keyId, string token)
        {
            return ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(EntityName).WithSigning(keyId, token));
        }

        private async Task InitializeWithNotesAsync(DatabaseType dbType)
        {
            await InitializeApplicationAsync(dbType, null, false);

            await AddEntityAsync(EntityName);
            await AddStringPropertyAsync(EntityName, nameof(Note.Text), required: false);
            AddSecurity(EntityName, Globals.AUTHENTICATED, actionType: SecurityActionType.get, properties: new() { nameof(Note.Text) });
        }

        private async Task AssertSignedRejectedAsync(long keyId, string token, string user)
        {
            (await ReadSignedAsync(keyId, token)).Match(
                r => throw new Exception($"The signing key of '{user}' was revoked but still authenticated"),
                e => Assert.Equal(ValidationError.UNAUTHORIZED, e.Code));
        }

        private async Task AssertAuthenticatesAsync(string token, string user)
        {
            (await ReadAsAsync(token)).Match(r => r, e => throw new Exception($"The token of '{user}' was rejected | {e.Code} | {e.Message}"));
        }

        private async Task AssertRejectedAsync(string token, string user)
        {
            (await ReadAsAsync(token)).Match(
                r => throw new Exception($"The token of '{user}' no longer exists but still authenticated"),
                e => Assert.Equal(ValidationError.UNAUTHORIZED, e.Code));
        }

        private static string Quote(DatabaseType dbType, string name)
        {
            return dbType switch
            {
                DatabaseType.MySQL => $"`{name}`",
                DatabaseType.PostgreSQL => $"\"{name}\"",
                _ => $"[{name}]"
            };
        }

        // removeUser = false: the token row is removed directly. true: the user is removed and its tokens go with it.
        [Theory]
        [InlineData(DatabaseType.SQLLite, false)]
        [InlineData(DatabaseType.SQLLite, true)]
        [InlineData(DatabaseType.SQLServer, false)]
        [InlineData(DatabaseType.MySQL, false)]
        [InlineData(DatabaseType.PostgreSQL, false)]
        public async Task Token_Removed_Behind_The_Cache_Stops_Authenticating(DatabaseType dbType, bool removeUser)
        {
            await InitializeWithNotesAsync(dbType);

            // The cache re-checks the database once 10% of the token lifetime has passed: 6 seconds here
            TestApplication.AuthTokenExpireMinutes = 1;
            MockApplicationService(TestApplication);

            var (removedUserId, _, removedToken) = await RegisterAndLoginAsync("removed@test.com");
            var (_, _, keptToken) = await RegisterAndLoginAsync("kept@test.com");

            // Both tokens work, and both users are now cached
            await AssertAuthenticatesAsync(removedToken, "removed@test.com");
            await AssertAuthenticatesAsync(keptToken, "kept@test.com");

            // Remove one token without going through the API's account endpoints
            var deleteSql = removeUser
                ? $"DELETE FROM {Quote(dbType, "Users")} WHERE {Quote(dbType, "ID")} = {removedUserId}"
                : $"DELETE FROM {Quote(dbType, "AuthTokens")} WHERE {Quote(dbType, "Owner")} = {removedUserId}";

            AddCustomEndpoint("RemoveBehindTheCache", deleteSql);
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "RemoveBehindTheCache", type: SecurityTypes.CustomEndpoint))
            {
                (await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New("RemoveBehindTheCache")))
                    .Match(r => r, e => throw new Exception($"Delete failed | {e.Code} | {e.Message}"));
            }

            await Task.Delay(TimeSpan.FromSeconds(7));

            // The removed token is rejected as soon as the cache checks the database again, and stays rejected
            await AssertRejectedAsync(removedToken, "removed@test.com");
            await AssertRejectedAsync(removedToken, "removed@test.com");

            // The token that still exists keeps working across the refresh
            await AssertAuthenticatesAsync(keptToken, "kept@test.com");
            await AssertAuthenticatesAsync(keptToken, "kept@test.com");
        }

        [Fact]
        public async Task Role_Removed_Behind_The_Cache_Stops_Granting_Access()
        {
            const string roleEntity = "ManagerNotes";
            const string role = "manager";

            await InitializeWithNotesAsync(DatabaseType.SQLLite);

            await AddEntityAsync(roleEntity);
            await AddStringPropertyAsync(roleEntity, nameof(Note.Text), required: false);
            AddSecurity(roleEntity, role, actionType: SecurityActionType.get, properties: new() { nameof(Note.Text) });

            // The cache re-checks the database once 10% of the token lifetime has passed: 6 seconds here
            TestApplication.AuthTokenExpireMinutes = 1;
            MockApplicationService(TestApplication);

            var (userId, _, token) = await RegisterAndLoginAsync("manager@test.com");
            await SetUserRolesAsync(userId, new() { role }, DatabaseType.SQLLite);

            Task<Either<DataResponse<Note>, ApilaneError>> ReadAsManagerAsync()
            {
                return ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(roleEntity).WithAuthToken(token));
            }

            // The role grants access, and the user is now cached with it
            (await ReadAsManagerAsync()).Match(r => r, e => throw new Exception($"The manager was rejected | {e.Code} | {e.Message}"));

            // Take the role away without going through the API's account endpoints
            AddCustomEndpoint("RemoveRole", $"UPDATE [Users] SET [Roles] = NULL WHERE [ID] = {userId}");
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "RemoveRole", type: SecurityTypes.CustomEndpoint))
            {
                (await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New("RemoveRole")))
                    .Match(r => r, e => throw new Exception($"Update failed | {e.Code} | {e.Message}"));
            }

            await Task.Delay(TimeSpan.FromSeconds(7));

            // The first request after the wait refreshes the token and reads the user again: the role is
            // gone, while the token itself still authenticates
            (await ReadAsManagerAsync()).Match(
                r => throw new Exception("The role was removed but still grants access"),
                e => Assert.Equal(ValidationError.UNAUTHORIZED, e.Code));

            await AssertAuthenticatesAsync(token, "manager@test.com");
        }

        [Fact]
        public async Task SignedLogout_Revokes_The_Token()
        {
            await InitializeWithNotesAsync(DatabaseType.SQLLite);

            var (_, keyId, token) = await RegisterAndLoginAsync("signed@test.com");

            (await ReadSignedAsync(keyId, token)).Match(r => r, e => throw new Exception($"Signed read failed | {e.Code} | {e.Message}"));

            // A signed request never sends the token, so the API has to find it through the key id
            (await ApilaneService.AccountLogoutAsync(AccountLogoutRequest.New(false).WithSigning(keyId, token)))
                .Match(r => r, e => throw new Exception($"Signed logout failed | {e.Code} | {e.Message}"));

            await AssertRejectedAsync(token, "signed@test.com");
            await AssertSignedRejectedAsync(keyId, token, "signed@test.com");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Renew_Revokes_The_Old_Token_And_Answers_Like_Login(bool signed)
        {
            await InitializeWithNotesAsync(DatabaseType.SQLLite);

            var (userId, keyId, token) = await RegisterAndLoginAsync("renew@test.com");

            var request = signed
                ? AccountRenewAuthTokenRequest.New().WithSigning(keyId, token)
                : AccountRenewAuthTokenRequest.New().WithAuthToken(token);

            var renewed = (await ApilaneService.AccountRenewAuthTokenAsync<ApiUser>(request))
                .Match(r => r, e => throw new Exception($"Renew failed | {e.Code} | {e.Message}"));

            // The user, a new token and the id of that token
            Assert.Equal(userId, renewed.User.ID);
            Assert.Equal("renew@test.com", renewed.User.Email);
            Assert.True(Guid.TryParse(renewed.AuthToken, out _), $"Renew did not return a token | {renewed.AuthToken}");
            Assert.NotEqual(token, renewed.AuthToken);
            Assert.NotEqual(keyId, renewed.AuthTokenID);

            // The old token is gone, as a bearer token and as a signing key
            await AssertRejectedAsync(token, "renew@test.com");
            await AssertSignedRejectedAsync(keyId, token, "renew@test.com");

            // The new one works both ways
            await AssertAuthenticatesAsync(renewed.AuthToken, "renew@test.com");
            (await ReadSignedAsync(renewed.AuthTokenID, renewed.AuthToken))
                .Match(r => r, e => throw new Exception($"The renewed key was rejected | {e.Code} | {e.Message}"));
        }

        [Fact]
        public async Task Logout_With_A_Bad_Signature_Revokes_Nothing()
        {
            await InitializeWithNotesAsync(DatabaseType.SQLLite);

            var (_, keyId, token) = await RegisterAndLoginAsync("signed@test.com");

            // Knowing a key id is not enough: the request is signed with the wrong secret. The client is
            // told so, instead of believing it has logged out.
            (await ApilaneService.AccountLogoutAsync(AccountLogoutRequest.New(false).WithSigning(keyId, Guid.NewGuid().ToString()))).Match(
                r => throw new Exception("A logout with a bad signature reported success"),
                e => Assert.Equal(ValidationError.UNAUTHORIZED, e.Code));

            await AssertAuthenticatesAsync(token, "signed@test.com");
            (await ReadSignedAsync(keyId, token)).Match(r => r, e => throw new Exception($"The key was revoked by an unsigned logout | {e.Code} | {e.Message}"));
        }
    }
}
