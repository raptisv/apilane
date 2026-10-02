using Apilane.Common.Enums;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using Apilane.Net.Services;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// An authentication token belongs to the application that issued it. These tests run two
    /// applications on the same API host and prove that a token (or signing key) of one is worthless
    /// against the other, even when both hold a token with the same id.
    /// </summary>
    public class AuthTokenIsolationTests : AppicationTestsBase
    {
        public AuthTokenIsolationTests() : base(SuiteContext.Shared)
        {
        }

        private const string Password = "password";

        private static async Task<(long KeyId, string Token)> RegisterAndLoginAsync(ApilaneService service, string email)
        {
            var registerResult = await service.AccountRegisterAsync(
                AccountRegisterRequest.New(new RegisterItem { Email = email, Username = email, Password = Password }));
            registerResult.Match(r => r, e => throw new Exception($"Register failed | {email} | {e.Code} | {e.Message}"));

            var login = (await service.AccountLoginAsync<ApiUser>(
                AccountLoginRequest.New(new LoginItem { Email = email, Password = Password })))
                .Match(r => r, e => throw new Exception($"Login failed | {email} | {e.Code} | {e.Message}"));

            return (login.AuthTokenID, login.AuthToken);
        }

        private static async Task<string> GetUserEmailAsync(ApilaneService service, AccountUserDataRequest request)
        {
            var result = await service.GetAccountUserDataAsync<ApiUser>(request);

            return result.Match(r => r.User.Email, e => throw new Exception($"User data failed | {e.Code} | {e.Message}"));
        }

        [Fact]
        public async Task BearerToken_Of_One_Application_Is_Rejected_By_Another()
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);
            var (_, otherService) = await InitializeOtherApplicationAsync();

            // Both applications have a user with the same id (1)
            await RegisterAndLoginAsync(otherService, "victim@other.test");
            var (_, token) = await RegisterAndLoginAsync(ApilaneService, "attacker@own.test");

            // The token works where it was issued (this also loads it into the API's token cache)
            Assert.Equal("attacker@own.test", await GetUserEmailAsync(ApilaneService, AccountUserDataRequest.New().WithAuthToken(token)));

            // ...and must be rejected by the other application
            var crossResult = await otherService.GetAccountUserDataAsync<ApiUser>(AccountUserDataRequest.New().WithAuthToken(token));

            crossResult.Match(
                r => throw new Exception($"The other application accepted a foreign token and returned user '{r.User.Email}'"),
                e => Assert.Equal(ValidationError.UNAUTHORIZED, e.Code));
        }

        [Fact]
        public async Task SignedRequest_With_Another_Applications_Key_Is_Rejected()
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);
            var (_, otherService) = await InitializeOtherApplicationAsync();

            await RegisterAndLoginAsync(otherService, "victim@other.test");
            var (keyId, token) = await RegisterAndLoginAsync(ApilaneService, "attacker@own.test");

            // A signed request works where the key was issued (this also caches the key's secret)
            Assert.Equal("attacker@own.test", await GetUserEmailAsync(ApilaneService, AccountUserDataRequest.New().WithSigning(keyId, token)));

            // ...and the same key must not sign requests to the other application
            var crossResult = await otherService.GetAccountUserDataAsync<ApiUser>(AccountUserDataRequest.New().WithSigning(keyId, token));

            crossResult.Match(
                r => throw new Exception($"The other application accepted a foreign signing key and returned user '{r.User.Email}'"),
                e => Assert.Equal(ValidationError.UNAUTHORIZED, e.Code));
        }

        [Fact]
        public async Task SignedRequests_Work_In_Both_Applications_When_They_Share_A_Key_Id()
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);
            var (_, otherService) = await InitializeOtherApplicationAsync();

            // First login in each application: both get key id 1, with different secrets
            var (ownKeyId, ownToken) = await RegisterAndLoginAsync(ApilaneService, "first@own.test");
            var (otherKeyId, otherToken) = await RegisterAndLoginAsync(otherService, "first@other.test");
            Assert.Equal(ownKeyId, otherKeyId);

            // Each application verifies with its own secret, in either order
            Assert.Equal("first@own.test", await GetUserEmailAsync(ApilaneService, AccountUserDataRequest.New().WithSigning(ownKeyId, ownToken)));
            Assert.Equal("first@other.test", await GetUserEmailAsync(otherService, AccountUserDataRequest.New().WithSigning(otherKeyId, otherToken)));
            Assert.Equal("first@own.test", await GetUserEmailAsync(ApilaneService, AccountUserDataRequest.New().WithSigning(ownKeyId, ownToken)));
        }
    }
}
