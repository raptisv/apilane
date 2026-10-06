using Apilane.Common.Models;
using Apilane.Common.Services;
using Microsoft.Extensions.Logging;

namespace Apilane.UnitTests
{
    /// <summary>
    /// A confirmation or a password reset mail carries a live token link, so anyone who can read the logs
    /// (console, Graylog) could confirm accounts or reset passwords with it. The mail service may log who a
    /// mail went to, its subject and what became of it, never what is in it.
    /// </summary>
    [TestClass]
    public class EmailServiceLoggingTests
    {
        private const string Recipient = "reader@example.com";
        private const string Subject = "Reset Password";
        private const string SmtpPassword = "smtp-password-that-must-not-be-logged";

        // What the "Reset password" template becomes: a link with a one-time token in its query string.
        private readonly string _token = Guid.NewGuid().ToString();
        private readonly string _link;
        private readonly string _body;
        private readonly CapturingLogger<EmailService> _logger = new();

        public EmailServiceLoggingTests()
        {
            _link = $"https://api.example.com/App/{Guid.NewGuid()}/Account/Manage/ResetPassword?Token={_token}";
            _body = $"Please click on this <a href=\"{_link}\">link</a> to reset your password";
        }

        /// <summary>
        /// Sends a mail to a mail server that is down: nothing listens on port 1 of this machine, so the
        /// send fails at once.
        /// </summary>
        private void SendMail(string subject)
        {
            new EmailService(_logger).SendMail(new EmailInfo()
            {
                MailServer = "127.0.0.1",
                MailServerPort = 1,
                MailFromAddress = "noreply@example.com",
                MailFromDisplayName = "Example",
                MailUserName = "smtp-user",
                MailPassword = SmtpPassword,
                Recipients = new[] { Recipient },
                Subject = subject,
                Body = _body
            });
        }

        private static void AssertNoneContains(IEnumerable<LogEntry> entries, params string[] secrets)
        {
            foreach (var entry in entries)
            {
                foreach (var secret in secrets)
                {
                    if (entry.Text.Contains(secret))
                    {
                        Assert.Fail($"A {entry.Level} log entry contains '{secret}': {entry.Message}");
                    }
                }
            }
        }

        [TestMethod]
        public void SendMail_ServerDown_LogsTheRecipientTheSubjectAndTheFailure()
        {
            // Act
            SendMail(Subject);

            // Assert: what is left to read is enough to see who was written to and that it did not work
            Assert.IsTrue(_logger.Entries.Any(x => x.Text.Contains(Recipient)), "No log entry names the recipient.");
            Assert.IsTrue(_logger.Entries.Any(x => x.Text.Contains(Subject)), "No log entry names the subject.");
            Assert.IsTrue(_logger.Entries.Any(x => x.Level == LogLevel.Error && x.Exception is not null), "The failure is not logged as an error with its exception.");
        }

        [TestMethod]
        public void SendMail_ServerDown_InformationEntriesDoNotContainTheBodyTheLinkOrTheToken()
        {
            // The entry of a send that works ('sent', with the recipient and the subject) is not written here:
            // the SmtpClient is made inside SendMail and needs a mail server with TLS. What this pins is the
            // entry written before the send is tried, which is where the body used to be logged.

            // Act
            SendMail(Subject);

            // Assert
            AssertNoneContains(_logger.Entries.Where(x => x.Level < LogLevel.Error), _body, _link, _token);
        }

        [TestMethod]
        public void SendMail_ServerDown_ErrorEntriesDoNotContainTheBodyTheLinkOrTheToken()
        {
            // Act
            SendMail(Subject);

            // Assert
            var errors = _logger.Entries.Where(x => x.Level >= LogLevel.Error).ToList();
            Assert.IsNotEmpty(errors, "The failure is not logged as an error.");
            AssertNoneContains(errors, _body, _link, _token);
        }

        [TestMethod]
        public void SendMail_SubjectCarriesTheLink_NoEntryContainsTheLinkOrTheToken()
        {
            // The reset and confirmation placeholders are replaced in the subject as well as in the body,
            // so a template that puts the link in its subject makes the subject a secret too.

            // Act
            SendMail($"Reset your password: {_link}");

            // Assert
            AssertNoneContains(_logger.Entries, _link, _token);
        }

        [TestMethod]
        public void SendMail_ServerDown_DoesNotLogTheMailServerPassword()
        {
            // Act
            SendMail(Subject);

            // Assert
            AssertNoneContains(_logger.Entries, SmtpPassword);
        }
    }
}
