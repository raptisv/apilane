using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// Login allows a limited number of attempts per login name since its last success, so that passwords cannot
    /// be guessed against one account as fast as the server answers. A password set, or a user deleted, through
    /// the data API ends the user's sessions at once, as Account/ChangePassword does, instead of leaving the tokens
    /// handed out working until the token cache next checks the database.
    /// </summary>
    public class AccountProtectionTests : AppicationTestsBase
    {
        // The allowance of one login name (AccountAPI): 20 attempts in 5 minutes
        private const int LoginAttemptsPerName = 20;
        private const string Password = "password";
        private const string NewPassword = "new-password";
        private const string WrongPassword = "wrong-password";
        private const string EntityName = "Notes";

        public AccountProtectionTests() : base(SuiteContext.Shared)
        {
        }

        private class Note : DataItem
        {
            public string? Text { get; set; }
        }

        // ─── Login attempts ───────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Login_Attempts_Per_Name_Should_Be_Limited_Since_The_Last_Success(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            var victim = Address("guessed", dbType, useDiffEntity);
            var other = Address("bystander", dbType, useDiffEntity);
            await RegisterAsync(victim);
            await RegisterAsync(other);

            // A success clears the count: a user who mistypes now and then is never held up
            for (var i = 0; i < LoginAttemptsPerName - 1; i++)
            {
                Assert.Equal(ValidationError.ERROR, await LoginErrorAsync(victim, WrongPassword));
            }

            await LoginAsync(victim, Password);

            // A full allowance of wrong passwords ...
            for (var i = 0; i < LoginAttemptsPerName; i++)
            {
                Assert.Equal(ValidationError.ERROR, await LoginErrorAsync(victim, WrongPassword));
            }

            // ... and the name is refused before the password is checked, the right one included, in any case
            Assert.Equal(ValidationError.RATE_LIMIT_EXCEEDED, await LoginErrorAsync(victim, Password));
            Assert.Equal(ValidationError.RATE_LIMIT_EXCEEDED, await LoginErrorAsync(victim.ToUpperInvariant(), Password));

            // Another name of the application is not held up
            await LoginAsync(other, Password);
        }

        // ─── Sessions ─────────────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Password_Set_Through_The_Data_Api_Should_End_The_Sessions_Of_The_User(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeWithNotesAsync(dbType, connectionString, useDiffEntity);
            await AddStringPropertyAsync("Users", "Nickname", required: false);

            var victim = Address("reset", dbType, useDiffEntity);
            var other = Address("not-reset", dbType, useDiffEntity);
            var victimId = await RegisterAsync(victim);
            await RegisterAsync(other);
            var victimToken = await LoginAsync(victim, Password);
            var otherToken = await LoginAsync(other, Password);

            await AssertAuthenticatesAsync(victimToken, victim);
            await AssertAuthenticatesAsync(otherToken, other);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Users",
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.put,
                properties: new() { "Nickname", "Password" }))
            {
                // Another property leaves the sessions alone
                await PutUserAsync(new Dictionary<string, object?> { ["ID"] = victimId, ["Nickname"] = "nick" });
                await AssertAuthenticatesAsync(victimToken, victim);

                // A new password, named in any case, ends them before the call returns
                await PutUserAsync(new Dictionary<string, object?> { ["ID"] = victimId, ["password"] = NewPassword });
            }

            await AssertRejectedAsync(victimToken, victim);

            // The sessions of other users are not touched, and the new password is the one that signs in
            await AssertAuthenticatesAsync(otherToken, other);
            await AssertAuthenticatesAsync(await LoginAsync(victim, NewPassword), victim);
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task User_Deleted_Through_The_Data_Api_Should_Be_Signed_Out_At_Once(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeWithNotesAsync(dbType, connectionString, useDiffEntity);

            var victim = Address("deleted", dbType, useDiffEntity);
            var owner = Address("owns-records", dbType, useDiffEntity);
            var other = Address("not-deleted", dbType, useDiffEntity);
            var victimId = await RegisterAsync(victim);
            var ownerId = await RegisterAsync(owner);
            await RegisterAsync(other);
            var victimToken = await LoginAsync(victim, Password);
            var ownerToken = await LoginAsync(owner, Password);
            var otherToken = await LoginAsync(other, Password);

            // A record of the owner: the foreign key from Owner to Users keeps the owner from being deleted
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, EntityName,
                inRole: Globals.AUTHENTICATED, actionType: SecurityActionType.post, properties: new() { nameof(Note.Text) }))
            {
                (await ApilaneService.PostDataAsync(DataPostRequest.New(EntityName).WithAuthToken(ownerToken), new { Text = "mine" }))
                    .Match(ids => ids, error => throw new Exception($"Post failed | {error.Code} | {error.Message}"));
            }

            await AssertAuthenticatesAsync(victimToken, victim);
            await AssertAuthenticatesAsync(ownerToken, owner);
            await AssertAuthenticatesAsync(otherToken, other);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Users",
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.delete))
            {
                // A delete that fails ends no session
                var refused = await ApilaneService.DeleteDataAsync(DataDeleteRequest.New("Users", new List<long> { ownerId }));
                refused.Match(
                    ids => throw new Exception($"A user who owns records was deleted | {string.Join(",", ids)}"),
                    error => Assert.NotEqual(ValidationError.UNAUTHORIZED, error.Code));

                await AssertAuthenticatesAsync(ownerToken, owner);

                var deleted = await ApilaneService.DeleteDataAsync(DataDeleteRequest.New("Users", new List<long> { victimId }));
                deleted.Match(
                    ids => Assert.Equal(new[] { victimId }, ids),
                    error => throw new Exception($"Delete failed | {error.Code} | {error.Message}"));
            }

            // Not once the token cache next checks the database (after 10% of the token lifetime): now
            await AssertRejectedAsync(victimToken, victim);
            await AssertAuthenticatesAsync(ownerToken, owner);
            await AssertAuthenticatesAsync(otherToken, other);
        }

        // ─── Application and calls ────────────────────────────────────────────────

        /// <summary>
        /// An address of one case of one test. The limiter keeps its counts for the application token, which every
        /// test of this class shares, so no two cases may count attempts for the same name.
        /// </summary>
        private static string Address(string name, DatabaseType dbType, bool useDiffEntity)
        {
            return $"{name}-{dbType}-{useDiffEntity}@test.com".ToLowerInvariant();
        }

        private async Task InitializeWithNotesAsync(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(EntityName);
            await AddStringPropertyAsync(EntityName, nameof(Note.Text), required: false);
            AddSecurity(EntityName, Globals.AUTHENTICATED, actionType: SecurityActionType.get, properties: new() { nameof(Note.Text) });
        }

        private async Task<long> RegisterAsync(string email)
        {
            var register = await ApilaneService.AccountRegisterAsync(
                AccountRegisterRequest.New(new RegisterItem { Email = email, Username = email, Password = Password }));

            return register.Match(
                userId => userId,
                error => throw new Exception($"Register failed | {email} | {error.Code} | {error.Message}"));
        }

        private async Task<string> LoginAsync(string email, string password)
        {
            var login = await ApilaneService.AccountLoginAsync<ApiUser>(
                AccountLoginRequest.New(new LoginItem { Email = email, Password = password }));

            return login.Match(
                success => success.AuthToken,
                error => throw new Exception($"Login failed | {email} | {error.Code} | {error.Message}"));
        }

        private async Task<ValidationError> LoginErrorAsync(string email, string password)
        {
            var login = await ApilaneService.AccountLoginAsync<ApiUser>(
                AccountLoginRequest.New(new LoginItem { Email = email, Password = password }));

            return login.Match<ValidationError>(
                success => throw new Exception($"The login of '{email}' succeeded"),
                error => error.Code);
        }

        private async Task PutUserAsync(object body)
        {
            var put = await ApilaneService.PutDataAsync(DataPutRequest.New("Users"), body);

            put.Match(
                affected => Assert.Equal(1, affected),
                error => throw new Exception($"Put failed | {error.Code} | {error.Message} | {error.Property}"));
        }

        // The probe is a read that only authenticated users may do: it depends on nothing but the token being accepted
        private async Task AssertAuthenticatesAsync(string token, string user)
        {
            var read = await ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(EntityName).WithAuthToken(token));

            read.Match(
                response => Assert.NotNull(response.Data),
                error => throw new Exception($"The token of '{user}' was rejected | {error.Code} | {error.Message}"));
        }

        private async Task AssertRejectedAsync(string token, string user)
        {
            var read = await ApilaneService.GetDataAsync<Note>(DataGetListRequest.New(EntityName).WithAuthToken(token));

            read.Match(
                response => throw new Exception($"The session of '{user}' should have ended but still authenticated"),
                error => Assert.Equal(ValidationError.UNAUTHORIZED, error.Code));
        }
    }
}
