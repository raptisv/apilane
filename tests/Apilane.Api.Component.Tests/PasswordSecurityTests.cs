using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Api.Core.Services;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Security;
using Apilane.Data.Repository.Factory;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    public class PasswordSecurityTests : AppicationTestsBase
    {
        private const string OriginalPassword = "Passw0rd!";
        // Exercise punctuation through the request body, encryption and the next login.
        private const string NewPassword = "N3w'password!";
        private const string Email = "password@test.com";
        private const string EntityName = "PrivateNotes";

        private record Session(long UserId, long KeyId, string Token);

        public PasswordSecurityTests() : base(SuiteContext.Shared)
        {
        }

        private async Task InitializeAsync(DatabaseType databaseType = DatabaseType.SQLLite)
        {
            await InitializeApplicationAsync(databaseType, null, false);
            TestApplication.ForceSingleLogin = false;
            TestApplication.AuthTokenExpireMinutes = 60;
            MockApplicationService(TestApplication);
            await AddEntityAsync(EntityName);
            AddSecurity(EntityName, Globals.AUTHENTICATED, actionType: SecurityActionType.get);
        }

        private async Task<Session> RegisterAndLoginAsync(string email)
        {
            (await ApilaneService.AccountRegisterAsync(AccountRegisterRequest.New(new RegisterItem
            {
                Username = email,
                Email = email,
                Password = OriginalPassword
            }))).Match(r => r, e => throw new Exception($"Register failed | {e.Code} | {e.Message}"));

            return await LoginAsync(email, OriginalPassword);
        }

        private async Task<Session> LoginAsync(string email, string password)
        {
            var login = (await ApilaneService.AccountLoginAsync<ApiUser>(AccountLoginRequest.New(new LoginItem
            {
                Email = email,
                Password = password
            }))).Match(r => r, e => throw new Exception($"Login failed | {e.Code} | {e.Message}"));

            return new Session(login.User.ID, login.AuthTokenID, login.AuthToken);
        }

        private async Task AssertAuthenticatesAsync(Session session, bool expected)
        {
            // Both paths are warmed before revocation. No delay: a cached token must stop working
            // before the password-change/reset/logout response completes, not at its next refresh.
            foreach (var signed in new[] { false, true })
            {
                var request = signed
                    ? DataGetListRequest.New(EntityName).WithSigning(session.KeyId, session.Token)
                    : DataGetListRequest.New(EntityName).WithAuthToken(session.Token);
                var result = await ApilaneService.GetDataAsync<DataItem>(request);
                if (expected)
                {
                    result.Match(r => r, e => throw new Exception($"Session rejected | {e.Code} | {e.Message}"));
                }
                else
                {
                    result.Match(r => throw new Exception("A revoked session still authenticated"),
                        e => Assert.Equal(ValidationError.UNAUTHORIZED, e.Code));
                }
            }
        }

        private async Task<HttpResponseMessage> ChangePasswordAsync(Session session, string currentPassword, string newPassword, bool signed = false)
        {
            var path = $"/api/Account/ChangePassword?appToken={TestApplication.Token}";
            var json = JsonSerializer.Serialize(new { Password = currentPassword, NewPassword = newPassword });
            using var request = new HttpRequestMessage(HttpMethod.Put, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            if (signed)
            {
                var timestamp = Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow).ToString();
                var canonical = RequestSignature.BuildCanonicalString(session.KeyId.ToString(), "PUT", path,
                    timestamp, Encoding.UTF8.GetBytes(json));
                request.Headers.Add(Globals.AuthKeyIdHeaderName, session.KeyId.ToString());
                request.Headers.Add(Globals.AuthTimestampHeaderName, timestamp);
                request.Headers.Add(Globals.AuthSignatureHeaderName, RequestSignature.ComputeSignature(session.Token, canonical));
            }
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            }

            return await HttpClient.SendAsync(request);
        }

        private async Task<string[]> CreateResetTokensAsync(long userId)
        {
            await using var store = new ApplicationDataStoreFactory(TestApplication.ToDbInfo(ApiConfiguration.FilesPath));
            var helper = new ApplicationHelperService(store);
            var tokens = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
            foreach (var token in tokens)
            {
                await helper.CreatePasswordResetTokenAsync(userId, token);
            }

            return tokens;
        }

        private async Task AssertResetTokensAsync(string[] tokens, long? expectedOwner)
        {
            await using var store = new ApplicationDataStoreFactory(TestApplication.ToDbInfo(ApiConfiguration.FilesPath));
            var helper = new ApplicationHelperService(store);
            foreach (var token in tokens)
            {
                Assert.Equal(expectedOwner, await helper.GetUserIdFromPasswordResetTokenAsync(token));
            }
        }

        private async Task AssertPasswordChangedAsync()
        {
            (await ApilaneService.AccountLoginAsync<ApiUser>(AccountLoginRequest.New(new LoginItem
            {
                Email = Email,
                Password = OriginalPassword
            }))).Match(r => throw new Exception("The previous password still authenticated"),
                e => Assert.Equal(ValidationError.ERROR, e.Code));

            await AssertAuthenticatesAsync(await LoginAsync(Email, NewPassword), true);
        }

        [Theory]
        [InlineData(DatabaseType.SQLLite, false)]
        [InlineData(DatabaseType.SQLLite, true)]
        [InlineData(DatabaseType.SQLServer, false)]
        [InlineData(DatabaseType.MySQL, false)]
        [InlineData(DatabaseType.PostgreSQL, false)]
        public async Task ChangePassword_ValidCurrentPassword_RevokesEverySessionAndResetLink(DatabaseType databaseType, bool signed)
        {
            await InitializeAsync(databaseType);
            var first = await RegisterAndLoginAsync(Email);
            var second = await LoginAsync(Email, OriginalPassword);
            var otherUser = await RegisterAndLoginAsync("other@test.com");
            var resetTokens = await CreateResetTokensAsync(first.UserId);
            await AssertAuthenticatesAsync(first, true);
            await AssertAuthenticatesAsync(second, true);
            await AssertAuthenticatesAsync(otherUser, true);

            using var response = await ChangePasswordAsync(first, OriginalPassword, NewPassword, signed);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", await response.Content.ReadAsStringAsync());

            await AssertAuthenticatesAsync(first, false);
            await AssertAuthenticatesAsync(second, false);
            await AssertAuthenticatesAsync(otherUser, true);
            await AssertResetTokensAsync(resetTokens, null);
            await AssertPasswordChangedAsync();
        }

        [Theory]
        [InlineData("WrongPassword!", NewPassword)]
        [InlineData(OriginalPassword, "short")]
        public async Task ChangePassword_InvalidPassword_PreservesSessionsAndResetLinks(string currentPassword, string newPassword)
        {
            await InitializeAsync();
            var first = await RegisterAndLoginAsync(Email);
            var second = await LoginAsync(Email, OriginalPassword);
            var resetTokens = await CreateResetTokensAsync(first.UserId);
            await AssertAuthenticatesAsync(first, true);
            await AssertAuthenticatesAsync(second, true);

            using var response = await ChangePasswordAsync(first, currentPassword, newPassword);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("VALIDATION", await response.Content.ReadAsStringAsync());

            await AssertAuthenticatesAsync(first, true);
            await AssertAuthenticatesAsync(second, true);
            await AssertResetTokensAsync(resetTokens, first.UserId);
            await LoginAsync(Email, OriginalPassword);
        }

        [Fact]
        public async Task ResetPassword_ValidLink_RevokesEverySessionAndOutstandingLink()
        {
            await InitializeAsync();
            var first = await RegisterAndLoginAsync(Email);
            var second = await LoginAsync(Email, OriginalPassword);
            var otherUser = await RegisterAndLoginAsync("other@test.com");
            var resetTokens = await CreateResetTokensAsync(first.UserId);
            var otherResetTokens = await CreateResetTokensAsync(otherUser.UserId);
            await AssertAuthenticatesAsync(first, true);
            await AssertAuthenticatesAsync(second, true);
            await AssertAuthenticatesAsync(otherUser, true);

            var browser = CreateHttpClient();
            var path = $"/App/{TestApplication.Token}/Account/Manage/ResetPassword?Token={resetTokens[0]}";
            var form = await browser.GetStringAsync(path);
            var antiForgeryToken = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(antiForgeryToken);
            using var response = await browser.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = Email,
                ["Password"] = NewPassword,
                ["ConfirmPassword"] = NewPassword,
                ["__RequestVerificationToken"] = antiForgeryToken
            }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Your password has been reset", await response.Content.ReadAsStringAsync());

            await AssertAuthenticatesAsync(first, false);
            await AssertAuthenticatesAsync(second, false);
            await AssertAuthenticatesAsync(otherUser, true);
            await AssertResetTokensAsync(resetTokens, null);
            await AssertResetTokensAsync(otherResetTokens, otherUser.UserId);
            await AssertPasswordChangedAsync();
        }

        [Fact]
        public async Task LogoutEverywhere_MoreThanOneThousandSessions_RevokesAllOfThem()
        {
            await InitializeAsync();
            var first = await RegisterAndLoginAsync(Email);
            var otherUser = await RegisterAndLoginAsync("other@test.com");
            var extraTokens = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid().ToString()).ToArray();
            var created = Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow);
            await using var store = new ApplicationDataStoreFactory(TestApplication.ToDbInfo(ApiConfiguration.FilesPath));
            await store.ExecuteCustomAsync("INSERT INTO [AuthTokens] ([Owner], [Created], [Token]) VALUES " +
                string.Join(",", extraTokens.Select(token => $"({first.UserId}, {created}, '{token}')")));
            var rows = await store.ExecuteCustomAsync($"SELECT [ID] FROM [AuthTokens] WHERE [Token] = '{extraTokens[^1]}'");
            var last = new Session(first.UserId, Utils.GetLong(rows.Single().Single()["ID"]), extraTokens[^1]);
            await AssertAuthenticatesAsync(first, true);
            await AssertAuthenticatesAsync(last, true);
            await AssertAuthenticatesAsync(otherUser, true);

            var revoked = (await ApilaneService.AccountLogoutAsync(AccountLogoutRequest.New(true).WithAuthToken(first.Token)))
                .Match(r => r, e => throw new Exception($"Logout failed | {e.Code} | {e.Message}"));
            Assert.Equal(1001, revoked);
            await AssertAuthenticatesAsync(first, false);
            await AssertAuthenticatesAsync(last, false);
            await AssertAuthenticatesAsync(otherUser, true);
        }
    }
}
