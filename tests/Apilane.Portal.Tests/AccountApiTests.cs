using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// Register, password reset and change password. The instance has one settings row, shared by
    /// every test class: each test sets what it needs (registration open or not, mail configured
    /// or not) and the seeded state is put back afterwards.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class AccountApiTests : IAsyncLifetime
    {
        private const string RegisterUrl = "/api/v1/account";
        private const string ResetRequestUrl = "/api/v1/account/password-reset-requests";
        private const string ResetUrl = "/api/v1/account/password-resets";
        private const string ChangePasswordUrl = "/api/v1/account/password";
        private const string SessionUrl = "/api/v1/session";

        private const string NewPassword = "a-new-password";

        private static readonly Regex _resetLink = new Regex("href=\"(https://portal.test/account/reset-password\\?code=([^\"&]+))\"", RegexOptions.Compiled);

        private readonly PortalFactory _portal;

        public AccountApiTests(PortalFactory portal)
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

        // ---------- Register ----------

        [Fact]
        public async Task Register_Should_Create_The_User_And_Sign_It_In_With_A_Session_Cookie()
        {
            var email = NewEmail();
            var before = DateTime.UtcNow.AddSeconds(-1);
            var client = _portal.CreateAnonymousClient();

            var response = await client.PostAsync(RegisterUrl, new { Email = email, Password = NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.False(response.SetsPersistentCookie(), "Register sets a session cookie, not a persistent one.");
            Assert.NotEqual(string.Empty, response.GetSetCookieHeader());
            Assert.DoesNotContain(NewPassword, await response.Content.ReadAsStringAsync());

            var session = await response.ReadJsonAsync<SessionResponse>();
            Assert.Equal(email, session.Email);
            Assert.False(session.IsAdmin);
            Assert.Equal("Apilane tests", session.InstanceTitle);

            // Signed in.
            Assert.Equal(email, (await (await client.GetAsync(SessionUrl)).ReadJsonAsync<SessionResponse>()).Email);

            // What registering stores.
            var user = await FindUserAsync(email);
            Assert.NotNull(user);
            Assert.Equal(email, user.UserName);
            Assert.True(user.DateRegistered >= before, "DateRegistered was not set.");
            Assert.True(user.LastLogin >= before, "LastLogin was not set by the sign-in.");
            Assert.False(string.IsNullOrWhiteSpace(user.AdminAuthToken));
            Assert.False(user.EmailConfirmed);
            Assert.NotEqual(NewPassword, user.PasswordHash);
            Assert.False(await _portal.WithDbContextAsync(db => db.UserRoles.AnyAsync(x => x.UserId == user.Id)), "A new user must have no role.");

            // And the password works on the sign-in endpoint.
            await _portal.SignInAsync(email, NewPassword);
        }

        [Fact]
        public async Task Register_When_Registration_Is_Off_Should_Return_403_And_Create_Nothing()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: false, mailConfigured: false);

            var email = NewEmail();
            var client = _portal.CreateAnonymousClient();

            // The body is checked first: a malformed one is 400, not 403.
            var invalid = await client.PostAsync(RegisterUrl, new { Email = email, Password = "short", ConfirmPassword = "short" }.ToJsonContent());
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(PortalErrorCode.Validation, (await invalid.ReadJsonAsync<ErrorResponse>()).Code);

            var response = await client.PostAsync(RegisterUrl, new { Email = email, Password = NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
            Assert.Equal(string.Empty, response.GetSetCookieHeader());
            Assert.Null(await FindUserAsync(email));
        }

        [Fact]
        public async Task Register_With_A_Taken_Email_Should_Return_400_With_One_Error_On_Email()
        {
            var (email, _) = await _portal.CreateUserAsync();

            var response = await _portal.CreateAnonymousClient().PostAsync(RegisterUrl, new { Email = email, Password = NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(string.Empty, response.GetSetCookieHeader());

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);

            var detail = Assert.Single(error.Errors ?? new List<ErrorDetail>());
            Assert.Equal("Email", detail.Property);
            Assert.Contains("already taken", detail.Message);
        }

        [Fact]
        public async Task Register_Should_Report_Each_Identity_Password_Rule_On_Password()
        {
            // The portal's own rule is length only, which the contract already checks. With
            // stricter Identity rules every broken one must come back as its own entry.
            var host = _portal.CreateHost(services => services.Configure<IdentityOptions>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
            }));

            var email = NewEmail();

            var response = await _portal.CreateAnonymousClient(host).PostAsync(RegisterUrl, new { Email = email, Password = "onlylowercase", ConfirmPassword = "onlylowercase" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal(2, error.Errors?.Count);
            Assert.All(error.Errors ?? new List<ErrorDetail>(), x => Assert.Equal("Password", x.Property));
            Assert.Null(await FindUserAsync(email));
        }

        [Theory]
        [InlineData("", NewPassword, NewPassword, "Email", "Required")]
        [InlineData("not-an-email", NewPassword, NewPassword, "Email", "Not a valid email address")]
        [InlineData("someone@portal.test", "short", "short", "Password", "Must be 8 to 100 characters")]
        [InlineData("someone@portal.test", NewPassword, "something-else", "ConfirmPassword", "The password and confirmation password do not match.")]
        public async Task Register_With_An_Invalid_Field_Should_Return_400_On_That_Property(string email, string password, string confirmPassword, string property, string message)
        {
            var response = await _portal.CreateAnonymousClient().PostAsync(RegisterUrl, new { Email = email, Password = password, ConfirmPassword = confirmPassword }.ToJsonContent());

            await AssertValidationAsync(response, property, message);
        }

        [Fact]
        public async Task Register_With_A_Password_Over_100_Characters_Should_Return_400_On_Password()
        {
            var password = new string('p', 101);

            var response = await _portal.CreateAnonymousClient().PostAsync(RegisterUrl, new { Email = NewEmail(), Password = password, ConfirmPassword = password }.ToJsonContent());

            await AssertValidationAsync(response, "Password", "Must be 8 to 100 characters");
        }

        [Fact]
        public async Task Register_Without_The_Csrf_Header_Should_Return_403_And_Create_Nothing()
        {
            var email = NewEmail();
            var client = _portal.CreateAnonymousClient();
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await client.PostAsync(RegisterUrl, new { Email = email, Password = NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
            Assert.Null(await FindUserAsync(email));
        }

        // ---------- Password-reset request ----------

        [Fact]
        public async Task ResetRequest_Without_Mail_Settings_Should_Return_409_And_Send_Nothing()
        {
            var (knownEmail, _) = await _portal.CreateUserAsync();
            var client = _portal.CreateAnonymousClient();

            var known = await client.PostAsync(ResetRequestUrl, new { Email = knownEmail }.ToJsonContent());
            var unknown = await client.PostAsync(ResetRequestUrl, new { Email = NewEmail() }.ToJsonContent());

            // The same answer for both: the mail check comes before the user lookup.
            foreach (var response in new[] { known, unknown })
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

                var error = await response.ReadJsonAsync<ErrorResponse>();
                Assert.Equal(PortalErrorCode.Conflict, error.Code);
                Assert.Equal("Email settings not set for instance, please contact admin.", error.Message);
            }

            Assert.Empty(_portal.Mail.SentTo(knownEmail));
        }

        [Fact]
        public async Task ResetRequest_Should_Return_The_Same_202_For_A_Known_And_An_Unknown_Email()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);

            var (knownEmail, _) = await _portal.CreateUserAsync();
            var unknownEmail = NewEmail();
            var client = _portal.CreateAnonymousClient();

            var known = await client.PostAsync(ResetRequestUrl, new { Email = knownEmail }.ToJsonContent());
            var unknown = await client.PostAsync(ResetRequestUrl, new { Email = unknownEmail }.ToJsonContent());

            foreach (var response in new[] { known, unknown })
            {
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            }

            Assert.Single(_portal.Mail.SentTo(knownEmail));
            Assert.Empty(_portal.Mail.SentTo(unknownEmail));
        }

        [Fact]
        public async Task ResetRequest_Should_Mail_A_Link_To_The_Ui_Whose_Code_Resets_The_Password()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);

            var (email, oldPassword) = await _portal.CreateUserAsync();
            var client = _portal.CreateAnonymousClient();

            await client.PostAsync(ResetRequestUrl, new { Email = email }.ToJsonContent());

            var mail = Assert.Single(_portal.Mail.SentTo(email));
            Assert.Equal("Reset password", mail.Subject);
            Assert.Equal("smtp.portal.test", mail.MailServer);
            Assert.Equal(587, mail.MailServerPort);
            Assert.Equal("portal@portal.test", mail.MailFromAddress);
            Assert.DoesNotContain("{PLACEHOLDER}", mail.Body);

            var link = _resetLink.Match(mail.Body);
            Assert.True(link.Success, "The mail has no link to /account/reset-password?code=...");

            var code = Uri.UnescapeDataString(link.Groups[2].Value);

            var response = await client.PostAsync(ResetUrl, new { Email = email, Code = code, Password = NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            // A reset does not sign in.
            Assert.Equal(string.Empty, response.GetSetCookieHeader());

            await _portal.SignInAsync(email, NewPassword);

            var oldSignIn = await client.PostAsync(SessionUrl, new { Email = email, Password = oldPassword }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Unauthorized, oldSignIn.StatusCode);

            // The code works once.
            var again = await client.PostAsync(ResetUrl, new { Email = email, Code = code, Password = "another-password", ConfirmPassword = "another-password" }.ToJsonContent());
            Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        }

        [Theory]
        [InlineData("", "Required")]
        [InlineData("not-an-email", "Not a valid email address")]
        public async Task ResetRequest_With_An_Invalid_Email_Should_Return_400(string email, string message)
        {
            // Mail is not configured here: a malformed e-mail is still 400, not 409.
            var response = await _portal.CreateAnonymousClient().PostAsync(ResetRequestUrl, new { Email = email }.ToJsonContent());

            await AssertValidationAsync(response, "Email", message);
        }

        [Fact]
        public async Task ResetRequest_Without_The_Csrf_Header_Should_Return_403_And_Send_Nothing()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);

            var (email, _) = await _portal.CreateUserAsync();
            var client = _portal.CreateAnonymousClient();
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await client.PostAsync(ResetRequestUrl, new { Email = email }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
            Assert.Empty(_portal.Mail.SentTo(email));
        }

        [Fact]
        public async Task ResetRequest_ForgedHostHeaders_Should_Keep_The_Configured_Public_Origin()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);
            var (email, _) = await _portal.CreateUserAsync();
            var client = _portal.CreateAnonymousClient();
            client.DefaultRequestHeaders.Host = "attacker.invalid";
            client.DefaultRequestHeaders.Add("X-Forwarded-Host", "attacker.invalid");
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");

            var response = await client.PostAsync(ResetRequestUrl, new { Email = email }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var mail = Assert.Single(_portal.Mail.SentTo(email));
            Assert.Matches(_resetLink, mail.Body);
            Assert.DoesNotContain("attacker.invalid", mail.Body);
        }

        // ---------- Password reset ----------

        [Fact]
        public async Task Reset_Should_Give_The_Same_400_For_A_Bad_Code_And_An_Unknown_Email()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var client = _portal.CreateAnonymousClient();

            var badCode = await client.PostAsync(ResetUrl, new { Email = email, Code = "not-a-code", Password = NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());
            var unknownEmail = await client.PostAsync(ResetUrl, new { Email = NewEmail(), Code = "not-a-code", Password = NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            foreach (var response in new[] { badCode, unknownEmail })
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

                var error = await response.ReadJsonAsync<ErrorResponse>();
                Assert.Equal(PortalErrorCode.Validation, error.Code);
                Assert.Equal(PortalApiErrors.ValidationMessage, error.Message);

                var detail = Assert.Single(error.Errors ?? new List<ErrorDetail>());
                Assert.Equal("Code", detail.Property);
                Assert.Equal("Invalid token.", detail.Message);
            }

            // Nothing changed for the real user.
            await _portal.SignInAsync(email, password);
        }

        [Theory]
        [InlineData("someone@portal.test", "", NewPassword, NewPassword, "Code", "Required")]
        [InlineData("not-an-email", "code", NewPassword, NewPassword, "Email", "Not a valid email address")]
        [InlineData("someone@portal.test", "code", "short", "short", "Password", "Must be 8 to 100 characters")]
        [InlineData("someone@portal.test", "code", NewPassword, "something-else", "ConfirmPassword", "The password and confirmation password do not match.")]
        public async Task Reset_With_An_Invalid_Field_Should_Return_400_On_That_Property(string email, string code, string password, string confirmPassword, string property, string message)
        {
            var response = await _portal.CreateAnonymousClient().PostAsync(ResetUrl, new { Email = email, Code = code, Password = password, ConfirmPassword = confirmPassword }.ToJsonContent());

            await AssertValidationAsync(response, property, message);
        }

        [Fact]
        public async Task Reset_With_A_Password_Over_100_Characters_Should_Return_400_On_Password()
        {
            var password = new string('p', 101);

            var response = await _portal.CreateAnonymousClient().PostAsync(ResetUrl, new { Email = NewEmail(), Code = "code", Password = password, ConfirmPassword = password }.ToJsonContent());

            await AssertValidationAsync(response, "Password", "Must be 8 to 100 characters");
        }

        [Fact]
        public async Task Reset_Should_Report_An_Identity_Password_Rule_On_Password()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);

            var host = _portal.CreateHost(services => services.Configure<IdentityOptions>(options => options.Password.RequireDigit = true));

            var (email, oldPassword) = await _portal.CreateUserAsync();
            var client = _portal.CreateAnonymousClient(host);
            var code = await RequestResetCodeAsync(client, email);

            var response = await client.PostAsync(ResetUrl, new { Email = email, Code = code, Password = "no-digit-here", ConfirmPassword = "no-digit-here" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal("Password", Assert.Single(error.Errors ?? new List<ErrorDetail>()).Property);

            // Nothing changed for the user.
            await _portal.SignInAsync(email, oldPassword);
        }

        [Fact]
        public async Task Reset_Should_End_The_Sessions_The_User_Has()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);

            var (email, oldPassword) = await _portal.CreateUserAsync();
            var session = await _portal.SignInAsync(email, oldPassword);
            var client = _portal.CreateAnonymousClient();
            var code = await RequestResetCodeAsync(client, email);

            Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync(SessionUrl)).StatusCode);

            var response = await client.PostAsync(ResetUrl, new { Email = email, Code = code, Password = NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await session.Client.GetAsync(SessionUrl)).StatusCode);
            Assert.Null((await FindUserAsync(email))?.AdminAuthToken);
        }

        [Fact]
        public async Task Reset_Without_The_Csrf_Header_Should_Return_403()
        {
            var client = _portal.CreateAnonymousClient();
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await client.PostAsync(ResetUrl, new { Email = NewEmail(), Code = "code", Password = NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        // ---------- Change password ----------

        [Fact]
        public async Task ChangePassword_Should_Keep_The_Caller_Signed_In_And_End_Other_Copies_Of_The_Session()
        {
            var (email, oldPassword) = await _portal.CreateUserAsync();
            var session = await _portal.SignInAsync(email, oldPassword);

            var response = await session.Client.PutAsync(ChangePasswordUrl, new { OldPassword = oldPassword, NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.NotEqual(string.Empty, response.GetSetCookieHeader());
            Assert.False(response.SetsPersistentCookie(), "Change password sets a session cookie, not a persistent one.");

            // The client holds the new cookie and is still signed in.
            var get = await session.Client.GetAsync(SessionUrl);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal(email, (await get.ReadJsonAsync<SessionResponse>()).Email);

            // The cookie from before the change is worthless.
            var cookieless = _portal.CreateCookielessClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await cookieless.GetWithCookieAsync(SessionUrl, session.Cookie)).StatusCode);

            // Only the new password signs in.
            var anonymous = _portal.CreateAnonymousClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(SessionUrl, new { Email = email, Password = oldPassword }.ToJsonContent())).StatusCode);
            await _portal.SignInAsync(email, NewPassword);

            // No mail settings, no mail.
            Assert.Empty(_portal.Mail.SentTo(email));
        }

        [Fact]
        public async Task ChangePassword_With_Mail_Settings_Should_Mail_A_Notice_With_A_Link_To_The_Ui()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);

            var (email, oldPassword) = await _portal.CreateUserAsync();
            var session = await _portal.SignInAsync(email, oldPassword);

            var response = await session.Client.PutAsync(ChangePasswordUrl, new { OldPassword = oldPassword, NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            var mail = Assert.Single(_portal.Mail.SentTo(email));
            Assert.Equal("Password changed", mail.Subject);
            Assert.Contains("href=\"https://portal.test/account/forgot-password\"", mail.Body);
            Assert.DoesNotContain(NewPassword, mail.Body);
            Assert.DoesNotContain(oldPassword, mail.Body);
        }

        [Fact]
        public async Task ChangePassword_With_A_Wrong_Old_Password_Should_Return_400_On_OldPassword_And_Change_Nothing()
        {
            var (email, oldPassword) = await _portal.CreateUserAsync();
            var session = await _portal.SignInAsync(email, oldPassword);

            var response = await session.Client.PutAsync(ChangePasswordUrl, new { OldPassword = "not-the-password", NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(string.Empty, response.GetSetCookieHeader());

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);

            var detail = Assert.Single(error.Errors ?? new List<ErrorDetail>());
            Assert.Equal("OldPassword", detail.Property);
            Assert.Equal("Incorrect password.", detail.Message);

            // Still signed in, still the old password.
            Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync(SessionUrl)).StatusCode);
            await _portal.SignInAsync(email, oldPassword);
        }

        [Fact]
        public async Task ChangePassword_Should_Report_An_Identity_Password_Rule_On_NewPassword()
        {
            var host = _portal.CreateHost(services => services.Configure<IdentityOptions>(options => options.Password.RequireDigit = true));

            var (email, oldPassword) = await _portal.CreateUserAsync();
            var session = await _portal.SignInAsync(email, oldPassword, host);

            var response = await session.Client.PutAsync(ChangePasswordUrl, new { OldPassword = oldPassword, NewPassword = "no-digit-here", ConfirmPassword = "no-digit-here" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal("NewPassword", Assert.Single(error.Errors ?? new List<ErrorDetail>()).Property);
        }

        [Theory]
        [InlineData("", NewPassword, NewPassword, "OldPassword", "Required")]
        [InlineData("old-password", "short", "short", "NewPassword", "Must be 8 to 100 characters")]
        [InlineData("old-password", NewPassword, "something-else", "ConfirmPassword", "The new password and confirmation password do not match.")]
        public async Task ChangePassword_With_An_Invalid_Field_Should_Return_400_On_That_Property(string oldPassword, string newPassword, string confirmPassword, string property, string message)
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.PutAsync(ChangePasswordUrl, new { OldPassword = oldPassword, NewPassword = newPassword, ConfirmPassword = confirmPassword }.ToJsonContent());

            await AssertValidationAsync(response, property, message);
        }

        [Fact]
        public async Task ChangePassword_With_A_Password_Over_100_Characters_Should_Return_400_On_NewPassword()
        {
            var client = await _portal.CreateUserClientAsync();
            var password = new string('p', 101);

            var response = await client.PutAsync(ChangePasswordUrl, new { OldPassword = "old-password", NewPassword = password, ConfirmPassword = password }.ToJsonContent());

            await AssertValidationAsync(response, "NewPassword", "Must be 8 to 100 characters");
        }

        [Fact]
        public async Task ChangePassword_Anonymous_Should_Return_401()
        {
            var response = await _portal.CreateAnonymousClient().PutAsync(ChangePasswordUrl, new { OldPassword = "x", NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task ChangePassword_After_Signing_In_Elsewhere_Should_Return_401_And_Change_Nothing()
        {
            var (email, oldPassword) = await _portal.CreateUserAsync();

            var stale = await _portal.SignInAsync(email, oldPassword);
            await _portal.SignInAsync(email, oldPassword);

            var response = await stale.Client.PutAsync(ChangePasswordUrl, new { OldPassword = oldPassword, NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            await _portal.SignInAsync(email, oldPassword);
        }

        [Fact]
        public async Task ChangePassword_Without_The_Csrf_Header_Should_Return_403_And_Change_Nothing()
        {
            var (email, oldPassword) = await _portal.CreateUserAsync();
            var session = await _portal.SignInAsync(email, oldPassword);
            session.Client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await session.Client.PutAsync(ChangePasswordUrl, new { OldPassword = oldPassword, NewPassword, ConfirmPassword = NewPassword }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
            await _portal.SignInAsync(email, oldPassword);
        }

        // One entry: the property and its message.
        private static async Task AssertValidationAsync(HttpResponseMessage response, string property, string message)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);

            var detail = Assert.Single(error.Errors ?? new List<ErrorDetail>());
            Assert.Equal(property, detail.Property);
            Assert.Equal(message, detail.Message);
        }

        // Asks for a reset and returns the code from the link in the mail.
        private async Task<string> RequestResetCodeAsync(HttpClient client, string email)
        {
            await client.PostAsync(ResetRequestUrl, new { Email = email }.ToJsonContent());

            var link = _resetLink.Match(Assert.Single(_portal.Mail.SentTo(email)).Body);
            Assert.True(link.Success, "The mail has no link to /account/reset-password?code=...");

            return Uri.UnescapeDataString(link.Groups[2].Value);
        }

        private static string NewEmail()
        {
            return $"new-{Guid.NewGuid():N}@portal.test";
        }

        private Task<ApplicationUser?> FindUserAsync(string email)
        {
            return _portal.WithDbContextAsync(db => db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email));
        }
    }
}
