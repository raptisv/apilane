using Apilane.Api.Component.Tests.Extensions;
using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// A mail to an address can be asked for by anyone, so the API lets one address have one mail of a kind
    /// every 5 minutes (Email/ForgotPassword, Email/RequestConfirmation). The forgot-password page that the
    /// Portal's Security screen links to asks for the same mail, so it has to keep the same limit, from
    /// the same allowance, and answer a limited request the same way whether the address has an account
    /// or not. Nothing on the page's controller may tell the two apart either.
    /// </summary>
    public class ForgotPasswordRateLimitTests : AppicationTestsBase
    {
        private const string UserPassword = "Passw0rd!";
        private const string ResetLinkPath = "/Account/Manage/ResetPassword?Token=";

        public ForgotPasswordRateLimitTests() : base(SuiteContext.Shared)
        {
        }

        private static Mailbox Mailbox => SuiteContext.Shared.Mailbox;

        /// <summary>
        /// What a browser shows after the form of the page was posted.
        /// </summary>
        private record PageAnswer(HttpStatusCode Status, string Body)
        {
            public bool IsConfirmation => Status == HttpStatusCode.OK && Body.Contains("Please check your email");
        }

        /// <summary>
        /// Every test uses addresses of its own: the limit is kept per address, and the test classes share one host.
        /// </summary>
        private static string NewAddress(string prefix)
        {
            return $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}@test.com";
        }

        /// <summary>
        /// A valid address that is one character longer than an address can be.
        /// </summary>
        private static string NewTooLongAddress()
        {
            var local = $"{Guid.NewGuid():N}".PadRight(Globals.MaxEmailLength + 1 - "@test.com".Length, 'a');

            return $"{local}@test.com";
        }

        /// <summary>
        /// An application with a mail server, mail templates that are on and a user for each of the addresses.
        /// </summary>
        private Task SetUpApplicationAsync(params string[] userAddresses)
        {
            return SetUpApplicationAsync(true, userAddresses);
        }

        /// <param name="withMailServer">False leaves the SMTP settings of the application incomplete, as a new application has them.</param>
        private async Task SetUpApplicationAsync(bool withMailServer, string[] userAddresses)
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);

            foreach (var address in userAddresses)
            {
                (await ApilaneService.AccountRegisterAsync(AccountRegisterRequest.New(new RegisterItem()
                {
                    Username = address[..address.IndexOf('@')],
                    Email = address,
                    Password = UserPassword
                }))).Match(
                    userId => userId,
                    error => throw new Exception($"Could not register {address} | {error.Code} | {error.Message}"));
            }

            if (withMailServer)
            {
                SetUpMailServer();
            }

            // The templates are off until the owner turns them on. They are turned on after the users are
            // registered, or the confirmation mail of each registration would be one more mail to count.
            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var templates = await HttpClient.RequestAsync<List<EmailTemplateDto>>(HttpMethod.Get, $"/api/Email/GetEmails?appToken={TestApplication.Token}")
                    ?? throw new Exception("Could not read the email templates");

                foreach (var template in templates)
                {
                    template.Active = true;

                    (await HttpClient.RequestAsync(HttpMethod.Put, $"/api/Email/Update?appToken={TestApplication.Token}", template)).EnsureSuccessStatusCode();
                }
            }
        }

        /// <summary>
        /// The mail server does not exist: the mails of the API host go to the test mailbox.
        /// </summary>
        private void SetUpMailServer()
        {
            TestApplication.MailServer = "smtp.test";
            TestApplication.MailServerPort = 587;
            TestApplication.MailFromAddress = "noreply@test.com";
            TestApplication.MailFromDisplayName = "Test";
            TestApplication.MailUserName = "smtp-user";
            TestApplication.MailPassword = "smtp-password";
            MockApplicationService(TestApplication);
        }

        /// <summary>
        /// Opens the page, fills in the address and posts the form, as a browser does.
        /// </summary>
        private async Task<PageAnswer> SubmitPageAsync(HttpClient browser, string address)
        {
            var pageUrl = ApilaneService.UrlFor_Account_Manage_ForgotPassword();

            var form = await browser.GetStringAsync(pageUrl);
            var antiForgeryToken = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(antiForgeryToken);

            var response = await browser.PostAsync(pageUrl, new FormUrlEncodedContent(new Dictionary<string, string>()
            {
                ["Email"] = address,
                ["__RequestVerificationToken"] = antiForgeryToken
            }));

            return new PageAnswer(response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private async Task<(HttpStatusCode Status, string Body)> CallApiAsync(string url)
        {
            var response = await HttpClient.GetAsync(url);

            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static void AssertLimitedPage(PageAnswer answer)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, answer.Status);
            Assert.Contains("Too many requests", answer.Body);
            Assert.DoesNotContain("Please check your email", answer.Body);
        }

        private static void AssertLimitedApi((HttpStatusCode Status, string Body) answer)
        {
            Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
            Assert.Contains(ValidationError.RATE_LIMIT_EXCEEDED.ToString(), answer.Body);
        }

        [Fact]
        public async Task Page_FirstRequestOfAnAddress_SendsTheResetMail()
        {
            var address = NewAddress("first");
            await SetUpApplicationAsync(address);

            var answer = await SubmitPageAsync(CreateHttpClient(), address);

            Assert.True(answer.IsConfirmation, $"The page did not confirm | {answer.Status} | {answer.Body}");

            var mail = Assert.Single(Mailbox.SentTo(address));
            Assert.Contains(ResetLinkPath, mail.Body);
        }

        [Fact]
        public async Task Page_SameAddressAgain_IsLimitedAndSendsNoMail()
        {
            var address = NewAddress("again");
            await SetUpApplicationAsync(address);
            var browser = CreateHttpClient();

            var first = await SubmitPageAsync(browser, address);
            var second = await SubmitPageAsync(browser, address);

            // The harm is the mail: a request that is let through is one more mail to the address
            Assert.Single(Mailbox.SentTo(address));
            Assert.True(first.IsConfirmation, $"The first request did not confirm | {first.Status}");
            AssertLimitedPage(second);
        }

        [Fact]
        public async Task Page_SameAddressWrittenInAnotherCase_IsLimited()
        {
            // The address is looked up without regard to case, so these are one user (one inbox)
            var address = NewAddress("case");
            await SetUpApplicationAsync(address);
            var browser = CreateHttpClient();

            var first = await SubmitPageAsync(browser, address);
            var second = await SubmitPageAsync(browser, address.ToUpperInvariant());

            Assert.True(first.IsConfirmation, $"The first request did not confirm | {first.Status}");
            AssertLimitedPage(second);
            Assert.Single(Mailbox.SentTo(address));
        }

        [Fact]
        public async Task Page_OtherAddress_IsNotLimited()
        {
            var limited = NewAddress("limited");
            var other = NewAddress("other");
            await SetUpApplicationAsync(limited, other);
            var browser = CreateHttpClient();

            await SubmitPageAsync(browser, limited);
            await SubmitPageAsync(browser, limited);
            var answer = await SubmitPageAsync(browser, other);

            Assert.True(answer.IsConfirmation, $"Another address was refused | {answer.Status} | {answer.Body}");
            Assert.Single(Mailbox.SentTo(other));
        }

        [Fact]
        public async Task Page_AddressWithoutAnAccount_AnswersLikeOneWithAnAccount()
        {
            // What the page says must not tell whether an address has an account: not at the first
            // request, and not once the limit is reached.
            var known = NewAddress("known");
            var unknown = NewAddress("unknown");
            await SetUpApplicationAsync(known);

            var answers = new List<(PageAnswer First, PageAnswer Second)>();
            foreach (var address in new[] { known, unknown })
            {
                var browser = CreateHttpClient();
                answers.Add((await SubmitPageAsync(browser, address), await SubmitPageAsync(browser, address)));
            }

            foreach (var (first, second) in answers)
            {
                Assert.True(first.IsConfirmation, $"The first request did not confirm | {first.Status}");
                AssertLimitedPage(second);
            }

            Assert.Single(Mailbox.SentTo(known));
            Assert.Empty(Mailbox.SentTo(unknown));
        }

        [Fact]
        public async Task Page_AfterTheApiSentTheMail_IsLimited()
        {
            // The page and the API ask for the same mail: one allowance for both, or two requests a minute
            // would be one more way to flood an address
            var address = NewAddress("apithenpage");
            await SetUpApplicationAsync(address);

            var api = await CallApiAsync(ApilaneService.UrlFor_Email_ForgotPassword(address));
            var page = await SubmitPageAsync(CreateHttpClient(), address);

            Assert.Equal(HttpStatusCode.OK, api.Status);
            AssertLimitedPage(page);
            Assert.Single(Mailbox.SentTo(address));
        }

        [Fact]
        public async Task Api_AfterThePageSentTheMail_IsLimited()
        {
            var address = NewAddress("pagethenapi");
            await SetUpApplicationAsync(address);

            var page = await SubmitPageAsync(CreateHttpClient(), address);
            var api = await CallApiAsync(ApilaneService.UrlFor_Email_ForgotPassword(address));

            Assert.True(page.IsConfirmation, $"The page did not confirm | {page.Status}");
            AssertLimitedApi(api);
            Assert.Single(Mailbox.SentTo(address));
        }

        [Fact]
        public async Task Api_ForgotPasswordForTheSameAddressAgain_IsLimited()
        {
            var address = NewAddress("apiforgot");
            await SetUpApplicationAsync(address);

            var first = await CallApiAsync(ApilaneService.UrlFor_Email_ForgotPassword(address));
            var second = await CallApiAsync(ApilaneService.UrlFor_Email_ForgotPassword(address));

            Assert.Equal(HttpStatusCode.OK, first.Status);
            AssertLimitedApi(second);
            Assert.Single(Mailbox.SentTo(address));
        }

        [Fact]
        public async Task Api_RequestConfirmationForTheSameAddressAgain_IsLimited()
        {
            // The resend of the confirmation mail is the other entry point that sends a mail on request
            var address = NewAddress("resend");
            await SetUpApplicationAsync(address);

            var first = await CallApiAsync(ApilaneService.UrlFor_Email_RequestConfirmation(address));
            var second = await CallApiAsync(ApilaneService.UrlFor_Email_RequestConfirmation(address));

            Assert.Equal(HttpStatusCode.OK, first.Status);
            AssertLimitedApi(second);
            Assert.Single(Mailbox.SentTo(address));
        }

        [Fact]
        public async Task Page_IncompleteMailSettings_AnswersAlikeForAnAddressWithAndWithoutAnAccount()
        {
            // The mail is only tried for an address with an account, so a missing mail server must be found
            // before the lookup: otherwise the page shows an error for the one and a confirmation for the other.
            var known = NewAddress("known");
            var unknown = NewAddress("unknown");
            await SetUpApplicationAsync(false, new[] { known });

            var answerForKnown = await SubmitPageAsync(CreateHttpClient(), known);
            var answerForUnknown = await SubmitPageAsync(CreateHttpClient(), unknown);

            Assert.False(answerForKnown.IsConfirmation, "The page confirmed a mail that cannot be sent");
            Assert.Equal(answerForKnown, answerForUnknown);
            Assert.Empty(Mailbox.SentTo(known));
        }

        [Fact]
        public async Task Page_IncompleteMailSettings_DoesNotUseUpTheAllowanceOfTheAddress()
        {
            // A request that fails for the owner's missing mail server is not a mail: once the owner has set
            // the server up, the user's next request must not be told to wait 5 minutes for it
            var address = NewAddress("noslot");
            await SetUpApplicationAsync(false, new[] { address });

            await SubmitPageAsync(CreateHttpClient(), address);

            SetUpMailServer();

            var answer = await SubmitPageAsync(CreateHttpClient(), address);

            Assert.True(answer.IsConfirmation, $"The page refused the first request that could be sent | {answer.Status} | {answer.Body}");
            Assert.Single(Mailbox.SentTo(address));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Api_AddressLongerThanAnAddressCanBe_IsRefused(bool forgotPassword)
        {
            // The limiter keeps the address it was asked about, and keeps it for good, so what it is asked
            // about has a size limit (254 characters, the length of an address in RFC 5321)
            await SetUpApplicationAsync(NewAddress("long"));
            var address = NewTooLongAddress();

            var answer = await CallApiAsync(forgotPassword
                ? ApilaneService.UrlFor_Email_ForgotPassword(address)
                : ApilaneService.UrlFor_Email_RequestConfirmation(address));

            Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
            Assert.Contains(ValidationError.VALIDATION.ToString(), answer.Body);
        }

        [Fact]
        public async Task Page_AddressLongerThanAnAddressCanBe_IsRefused()
        {
            // The page takes the address from a form, which can be megabytes long: the limiter must not get it
            await SetUpApplicationAsync(NewAddress("long"));
            var address = NewTooLongAddress();

            var answer = await SubmitPageAsync(CreateHttpClient(), address);

            Assert.False(answer.IsConfirmation, $"The page accepted an address of {address.Length} characters");
            Assert.DoesNotContain("Too many requests", answer.Body);
            Assert.Empty(Mailbox.SentTo(address));
        }

        [Fact]
        public async Task Manage_GetUserIdByEmail_IsNotReachable()
        {
            // A public method of a controller is an action that anyone who knows the application token (it is
            // in every client app) can call. This helper of the reset page answered with the ID of the user of
            // an address, and with nothing for an unknown one: a way to list the users that the page's
            // 'a known and an unknown address answer alike' rules out.
            var known = NewAddress("known");
            var unknown = NewAddress("unknown");
            await SetUpApplicationAsync(known);
            var pageUrl = ApilaneService.UrlFor_Account_Manage_ForgotPassword();
            var helperUrl = pageUrl[..pageUrl.LastIndexOf('/')] + "/GetUserIdByEmail?userEmail=";

            var answerForKnown = await CallApiAsync(helperUrl + Uri.EscapeDataString(known));
            var answerForUnknown = await CallApiAsync(helperUrl + Uri.EscapeDataString(unknown));

            Assert.Equal(HttpStatusCode.NotFound, answerForKnown.Status);
            Assert.Equal(HttpStatusCode.NotFound, answerForUnknown.Status);
        }
    }
}
