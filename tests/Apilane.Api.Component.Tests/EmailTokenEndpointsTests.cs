using Apilane.Api.Component.Tests.Extensions;
using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common.Enums;
using Apilane.Common.Models.Dto;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// The placeholders {confirmation_token} and {reset_password_token} hand the token of a mail to the
    /// application, and Email/ConfirmEmail and Email/ResetPassword spend it, so a server can complete both flows
    /// without the hosted pages. The links of the mail (and the hosted pages) keep working.
    /// </summary>
    public class EmailTokenEndpointsTests : AppicationTestsBase
    {
        private const string OriginalPassword = "Passw0rd!";
        private const string NewPassword = "N3w'password!";

        // The templates the tests install: the token alone on one line, then the link of the mail
        private const string ConfirmationBody = "CODE:{confirmation_token}\nLINK:<a href=\"{confirmation_url}\">link</a>";
        private const string ResetBody = "CODE:{reset_password_token}\nLINK:<a href=\"{reset_password_url}\">link</a>";

        public EmailTokenEndpointsTests() : base(SuiteContext.Shared)
        {
        }

        private static Mailbox Mailbox => SuiteContext.Shared.Mailbox;

        private static string NewAddress(string prefix)
        {
            return $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}@test.com";
        }

        private async Task SetUpApplicationAsync(string address)
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);

            (await ApilaneService.AccountRegisterAsync(AccountRegisterRequest.New(new RegisterItem()
            {
                Username = address[..address.IndexOf('@')],
                Email = address,
                Password = OriginalPassword
            }))).Match(
                userId => userId,
                error => throw new Exception($"Could not register {address} | {error.Code} | {error.Message}"));

            TestApplication.MailServer = "smtp.test";
            TestApplication.MailServerPort = 587;
            TestApplication.MailFromAddress = "noreply@test.com";
            TestApplication.MailFromDisplayName = "Test";
            TestApplication.MailUserName = "smtp-user";
            TestApplication.MailPassword = "smtp-password";
            MockApplicationService(TestApplication);

            // The templates are off until the owner turns them on, after the user is registered
            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var templates = await HttpClient.RequestAsync<List<EmailTemplateDto>>(HttpMethod.Get, $"/api/Email/GetEmails?appToken={TestApplication.Token}")
                    ?? throw new Exception("Could not read the email templates");

                foreach (var template in templates)
                {
                    template.Active = true;
                    template.Content = template.EventCode == nameof(EmailEventsCodes.UserRegisterConfirmation) ? ConfirmationBody : ResetBody;

                    (await HttpClient.RequestAsync(HttpMethod.Put, $"/api/Email/Update?appToken={TestApplication.Token}", template)).EnsureSuccessStatusCode();
                }
            }
        }

        /// <summary>
        /// The token the mail carries twice: alone (the token placeholder) and in its link (the URL placeholder).
        /// </summary>
        private static (string Token, string TokenOfTheLink) ReadMail(string address)
        {
            var mail = Assert.Single(Mailbox.SentTo(address));
            var token = Regex.Match(mail.Body, @"CODE:([0-9a-f-]{36})").Groups[1].Value;
            var tokenOfTheLink = Regex.Match(mail.Body, @"LINK:.*?[?&][Tt]oken=([0-9a-f-]{36})").Groups[1].Value;

            Assert.NotEmpty(token);

            return (token, tokenOfTheLink);
        }

        private async Task<(HttpStatusCode Status, string Body)> PostAsync(string action, object? body)
        {
            var response = await HttpClient.RequestAsync(HttpMethod.Post, $"/api/Email/{action}?appToken={TestApplication.Token}", body);

            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private async Task<ApiUser> LoginAsync(string address, string password)
        {
            return (await ApilaneService.AccountLoginAsync<ApiUser>(AccountLoginRequest.New(new LoginItem()
            {
                Email = address,
                Password = password
            }))).Match(
                login => login.User,
                error => throw new Exception($"Login failed | {error.Code} | {error.Message}"));
        }

        private static void AssertError((HttpStatusCode Status, string Body) answer, ValidationError code, string property)
        {
            Assert.Equal(HttpStatusCode.BadRequest, answer.Status);

            using var json = JsonDocument.Parse(answer.Body);
            Assert.Equal(code.ToString(), json.RootElement.GetProperty("Code").GetString());
            Assert.Equal(property, json.RootElement.GetProperty("Property").GetString());
        }

        [Fact]
        public async Task ResetMail_CarriesTheTokenAloneAndTheLinkHasTheSameToken()
        {
            var address = NewAddress("reset-mail");
            await SetUpApplicationAsync(address);

            (await HttpClient.GetAsync($"/api/Email/ForgotPassword?appToken={TestApplication.Token}&email={address}")).EnsureSuccessStatusCode();

            var (token, tokenOfTheLink) = ReadMail(address);
            Assert.Equal(token, tokenOfTheLink);
            Assert.True(Guid.TryParse(token, out _), "The token is not URL-safe.");
        }

        [Fact]
        public async Task ResetPassword_WithTheTokenOfTheMail_ChangesThePasswordOnce()
        {
            var address = NewAddress("reset");
            await SetUpApplicationAsync(address);
            (await HttpClient.GetAsync($"/api/Email/ForgotPassword?appToken={TestApplication.Token}&email={address}")).EnsureSuccessStatusCode();
            var (token, _) = ReadMail(address);

            var answer = await PostAsync("ResetPassword", new { Token = token, Password = NewPassword });

            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.Equal("\"OK\"", answer.Body);
            await LoginAsync(address, NewPassword);
            await Assert.ThrowsAsync<Exception>(() => LoginAsync(address, OriginalPassword));

            // A successful reset invalidates every link, the one just used as well
            AssertError(await PostAsync("ResetPassword", new { Token = token, Password = "An0ther-password" }), ValidationError.VALIDATION, "Token");
        }

        [Fact]
        public async Task ResetPassword_WithATooShortPassword_AnswersWhatThePageShowsAndKeepsTheToken()
        {
            var address = NewAddress("short");
            await SetUpApplicationAsync(address);
            (await HttpClient.GetAsync($"/api/Email/ForgotPassword?appToken={TestApplication.Token}&email={address}")).EnsureSuccessStatusCode();
            var (token, _) = ReadMail(address);

            var answer = await PostAsync("ResetPassword", new { Token = token, Password = "short" });

            AssertError(answer, ValidationError.VALIDATION, "Password");
            Assert.Contains("The Password must be at least 8 characters long.", answer.Body);

            // The token was not spent
            Assert.Equal(HttpStatusCode.OK, (await PostAsync("ResetPassword", new { Token = token, Password = NewPassword })).Status);
        }

        [Theory]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        [InlineData("not-a-token")]
        public async Task ResetPassword_WithAnUnknownToken_AnswersAnInvalidTokenError(string token)
        {
            await SetUpApplicationAsync(NewAddress("unknown"));

            var answer = await PostAsync("ResetPassword", new { Token = token, Password = NewPassword });

            AssertError(answer, ValidationError.VALIDATION, "Token");
            Assert.Contains("Invalid or expired token", answer.Body);
        }

        [Fact]
        public async Task ResetPassword_WithoutTokenOrPassword_AnswersRequired()
        {
            await SetUpApplicationAsync(NewAddress("required"));

            AssertError(await PostAsync("ResetPassword", new { Password = NewPassword }), ValidationError.REQUIRED, "Token");
            AssertError(await PostAsync("ResetPassword", new { Token = Guid.NewGuid().ToString() }), ValidationError.REQUIRED, "Password");
            AssertError(await PostAsync("ResetPassword", null), ValidationError.REQUIRED, "Password");
        }

        [Fact]
        public async Task ConfirmEmail_WithTheTokenOfTheMail_ConfirmsTheUser()
        {
            var address = NewAddress("confirm");
            await SetUpApplicationAsync(address);
            Assert.False((await LoginAsync(address, OriginalPassword)).EmailConfirmed);
            (await HttpClient.GetAsync($"/api/Email/RequestConfirmation?appToken={TestApplication.Token}&email={address}")).EnsureSuccessStatusCode();
            var (token, tokenOfTheLink) = ReadMail(address);
            Assert.Equal(token, tokenOfTheLink);

            var answer = await PostAsync("ConfirmEmail", new { Token = token });

            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.Equal("\"OK\"", answer.Body);
            Assert.True((await LoginAsync(address, OriginalPassword)).EmailConfirmed);

            // As with the link, the token is not spent: asking again is not an error
            Assert.Equal(HttpStatusCode.OK, (await PostAsync("ConfirmEmail", new { Token = token })).Status);
        }

        [Fact]
        public async Task Sdk_ConfirmsTheEmailAndResetsThePasswordWithTheTokenOfTheMail()
        {
            var address = NewAddress("sdk");
            await SetUpApplicationAsync(address);
            (await HttpClient.GetAsync($"/api/Email/RequestConfirmation?appToken={TestApplication.Token}&email={address}")).EnsureSuccessStatusCode();
            var (confirmationToken, _) = ReadMail(address);
            (await ApilaneService.EmailConfirmEmailAsync(EmailConfirmEmailRequest.New(confirmationToken)))
                .Match(ok => ok, error => throw new Exception($"Confirm failed | {error.Code} | {error.Message}"));
            Assert.True((await LoginAsync(address, OriginalPassword)).EmailConfirmed);

            (await HttpClient.GetAsync($"/api/Email/ForgotPassword?appToken={TestApplication.Token}&email={address}")).EnsureSuccessStatusCode();
            var resetToken = Mailbox.SentTo(address).Select(x => Regex.Match(x.Body, @"CODE:([0-9a-f-]{36})").Groups[1].Value).Last();

            var tooShort = await ApilaneService.EmailResetPasswordAsync(EmailResetPasswordRequest.New(resetToken, "short"));
            Assert.True(tooShort.HasError(out var error));
            Assert.Equal(ValidationError.VALIDATION, error.Code);
            Assert.Equal("Password", error.Property);

            (await ApilaneService.EmailResetPasswordAsync(EmailResetPasswordRequest.New(resetToken, NewPassword)))
                .Match(ok => ok, e => throw new Exception($"Reset failed | {e.Code} | {e.Message}"));
            await LoginAsync(address, NewPassword);
        }

        [Fact]
        public async Task ConfirmEmail_WithAnUnknownToken_AnswersAnInvalidTokenError()
        {
            await SetUpApplicationAsync(NewAddress("confirm-unknown"));

            AssertError(await PostAsync("ConfirmEmail", new { Token = Guid.NewGuid().ToString() }), ValidationError.VALIDATION, "Token");
            AssertError(await PostAsync("ConfirmEmail", new { }), ValidationError.REQUIRED, "Token");
        }

        [Fact]
        public async Task TheLinkOfTheMail_StillConfirmsTheUser()
        {
            var address = NewAddress("link");
            await SetUpApplicationAsync(address);
            (await HttpClient.GetAsync($"/api/Email/RequestConfirmation?appToken={TestApplication.Token}&email={address}")).EnsureSuccessStatusCode();
            var mail = Assert.Single(Mailbox.SentTo(address));
            // Whatever the server address of the test application is, the link ends in the path of the endpoint
            var path = Regex.Match(mail.Body, @"(/api/Account/Confirm\?[^""]+)""").Groups[1].Value;
            Assert.NotEmpty(path);

            // It answers a redirect to the landing page, which the test host does not serve: only the effect counts
            await CreateHttpClient().GetAsync(path);

            Assert.True((await LoginAsync(address, OriginalPassword)).EmailConfirmed);
        }
    }
}
