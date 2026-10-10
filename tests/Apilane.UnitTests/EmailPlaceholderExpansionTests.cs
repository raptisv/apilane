using Apilane.Api.Core.Services;
using Apilane.Api.Core.Configuration;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Helpers;
using Apilane.Data.Abstractions;
using FakeItEasy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Apilane.UnitTests
{
    /// <summary>
    /// What a mail template becomes: the URL placeholders (unchanged) and the token placeholders that give an
    /// application the token alone, so it can build its own link. One mail has one token: the URL and the token
    /// placeholder of an event expand from the same one.
    /// </summary>
    [TestClass]
    public class EmailPlaceholderExpansionTests
    {
        private const string AppToken = "0b9a3f6c-6a3b-4f6e-9e0a-9f1f0b1a2c3d";
        private const string ServerUrl = "https://API.example.com/";

        private readonly List<string> _confirmationTokens = new();
        private readonly List<string> _resetTokens = new();
        private readonly ApplicationEmailService _service;

        public EmailPlaceholderExpansionTests()
        {
            var helper = A.Fake<IApplicationHelperService>();
            A.CallTo(() => helper.CreateEmailConfirmationTokenAsync(A<long>._, A<string>._))
                .Invokes((long _, string token) => _confirmationTokens.Add(token));
            A.CallTo(() => helper.CreatePasswordResetTokenAsync(A<long>._, A<string>._))
                .Invokes((long _, string token) => _resetTokens.Add(token));

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Url"] = "https://api.example.com",
                ["FilesPath"] = Path.GetTempPath(),
                ["PortalUrl"] = "https://portal.example.com",
                ["InstallationKey"] = "installation-key"
            }).Build();

            _service = new ApplicationEmailService(
                new ApiConfiguration(configuration), helper, A.Fake<IEmailService>(), A.Fake<IServiceScopeFactory>());
        }

        private static Dictionary<string, object?> User() => new()
        {
            ["ID"] = 7L,
            ["Username"] = "user",
            ["Email"] = "user@example.com"
        };

        private static EmailEvent Event(EmailEventsCodes code) => EmailEvent.EmailEvents.Single(x => x.Code == code);

        private Task<(string Subject, string Content)> ExpandAsync(EmailEventsCodes code, string subject, string content)
        {
            return _service.CreateSubjectAndContentAsync(AppToken, ServerUrl, Event(code), subject, content, User(), User());
        }

        [TestMethod]
        public async Task Confirmation_TokenPlaceholder_ExpandsToTheTokenAlone()
        {
            var (_, content) = await ExpandAsync(EmailEventsCodes.UserRegisterConfirmation, "Welcome", "Code: {confirmation_token}");

            var token = Assert.ContainsSingle(_confirmationTokens);
            Assert.AreEqual($"Code: {token}", content);
            Assert.IsTrue(Guid.TryParse(token, out _), "The token is not a GUID, so it is not URL-safe by construction.");
        }

        [TestMethod]
        public async Task Confirmation_UrlPlaceholder_ExpandsAsBefore()
        {
            var (_, content) = await ExpandAsync(EmailEventsCodes.UserRegisterConfirmation, "Welcome", "<a href=\"{confirmation_url}\">link</a>");

            var token = Assert.ContainsSingle(_confirmationTokens);
            Assert.AreEqual($"<a href=\"https://api.example.com/api/Account/Confirm?appToken={AppToken}&token={token}\">link</a>", content);
        }

        [TestMethod]
        public async Task Confirmation_UrlAndTokenPlaceholders_ShareOneToken()
        {
            var (subject, content) = await ExpandAsync(
                EmailEventsCodes.UserRegisterConfirmation, "Code {confirmation_token}", "{confirmation_url} {confirmation_token}");

            var token = Assert.ContainsSingle(_confirmationTokens);
            Assert.AreEqual($"Code {token}", subject);
            Assert.AreEqual($"https://api.example.com/api/Account/Confirm?appToken={AppToken}&token={token} {token}", content);
        }

        [TestMethod]
        public async Task Reset_TokenPlaceholder_ExpandsToTheTokenAlone()
        {
            var (_, content) = await ExpandAsync(EmailEventsCodes.UserForgotPassword, "Reset", "Code: {reset_password_token}");

            var token = Assert.ContainsSingle(_resetTokens);
            Assert.AreEqual($"Code: {token}", content);
        }

        [TestMethod]
        public async Task Reset_UrlPlaceholder_ExpandsAsBefore()
        {
            var (_, content) = await ExpandAsync(EmailEventsCodes.UserForgotPassword, "Reset", "{reset_password_url}");

            var token = Assert.ContainsSingle(_resetTokens);
            Assert.AreEqual($"https://api.example.com/App/{AppToken}/Account/Manage/ResetPassword?Token={token}", content);
        }

        [TestMethod]
        public async Task Reset_UrlAndTokenPlaceholders_ShareOneToken()
        {
            var (_, content) = await ExpandAsync(EmailEventsCodes.UserForgotPassword, "Reset", "{reset_password_url} {reset_password_token}");

            var token = Assert.ContainsSingle(_resetTokens);
            Assert.AreEqual($"https://api.example.com/App/{AppToken}/Account/Manage/ResetPassword?Token={token} {token}", content);
        }

        [TestMethod]
        public async Task Template_WithoutAnyTokenPlaceholder_StillCreatesItsOneToken()
        {
            // As before: the token of an event is created whether or not the template uses it
            await ExpandAsync(EmailEventsCodes.UserForgotPassword, "Reset", "No link");

            Assert.ContainsSingle(_resetTokens);
            Assert.IsEmpty(_confirmationTokens);
        }

        [TestMethod]
        public async Task EachEvent_ListsItsTokenPlaceholderNextToItsUrlPlaceholder()
        {
            CollectionAssert.AreEqual(
                new[] { EmailEventsPlaceholders.confirmation_url, EmailEventsPlaceholders.confirmation_token },
                Event(EmailEventsCodes.UserRegisterConfirmation).Placeholders);
            CollectionAssert.AreEqual(
                new[] { EmailEventsPlaceholders.reset_password_url, EmailEventsPlaceholders.reset_password_token },
                Event(EmailEventsCodes.UserForgotPassword).Placeholders);

            await Task.CompletedTask;
        }
    }
}
