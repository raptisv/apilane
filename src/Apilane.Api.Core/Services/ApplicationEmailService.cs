using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Helpers;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Services
{
    public class ApplicationEmailService : IApplicationEmailService
    {
        private readonly ApiConfiguration _apiConfiguration;
        private readonly IApplicationHelperService _applicationHelperService;
        private readonly IEmailService _emailService;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public ApplicationEmailService(
            ApiConfiguration apiConfiguration,
            IApplicationHelperService applicationHelperService,
            IEmailService emailService,
            IServiceScopeFactory serviceScopeFactory)
        {
            _applicationHelperService = applicationHelperService;
            _emailService = emailService;
            _apiConfiguration = apiConfiguration;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task SendEmailFromApplication_FireAndForgetAsync(
          string appToken,
          string applicationServerUrl,
          EmailSettings? emailSettings,
          EmailEventsCodes eventCode,
          Dictionary<string, object?> userThatAcceptsTheEmail,
          Dictionary<string, object?> userThatTriggeredTheEmail)
        {
            var emailTemplate = await _applicationHelperService.GetEmailAsync(appToken, eventCode);

            if (emailTemplate is not null)
            {
                var isActive = Utils.GetBool(emailTemplate.Active);

                if (isActive)
                {
                    if (emailSettings is null)
                    {
                        throw new ApilaneException(AppErrors.ERROR, $"Missing application SMTP settings.");
                    }

                    var emailEvent = EmailEvent.EmailEvents.Single(x => x.Code == eventCode);

                    var result = await CreateSubjectAndContentAsync(
                        appToken,
                        applicationServerUrl,
                        emailEvent,
                        Utils.GetString(emailTemplate.Subject),
                        Utils.GetString(emailTemplate.Content),
                        userThatAcceptsTheEmail,
                        userThatTriggeredTheEmail);

                    var emailTask = Task.Run(() =>
                    {
                        _emailService.SendMail(new EmailInfo()
                        {
                            MailFromAddress = emailSettings.MailFromAddress,
                            MailFromDisplayName = emailSettings.MailFromDisplayName,
                            MailPassword = emailSettings.MailPassword,
                            MailServer = emailSettings.MailServer,
                            MailServerPort = emailSettings.MailServerPort,
                            MailUserName = emailSettings.MailUserName!,
                            Subject = result.Subject,
                            Body = result.Content,
                            Recipients = new string[] { Utils.GetString(userThatAcceptsTheEmail[nameof(Users.Email)]) }
                        });
                    });
                }
            }
        }

        public async Task<bool> SendEmailFromApplicationAsync(
            string appToken,
            string applicationServerUrl,
            EmailSettings? emailSettings,
            EmailEventsCodes eventCode,
            Dictionary<string, object?> userThatAcceptsTheEmail,
            Dictionary<string, object?> userThatTriggeredTheEmail)
        {
            var emailTemplate = await _applicationHelperService.GetEmailAsync(appToken, eventCode);

            if (emailTemplate is not null)
            {
                if (emailTemplate.Active)
                {
                    if (emailSettings is null)
                    {
                        throw new ApilaneException(AppErrors.ERROR, $"Missing application SMTP settings.");
                    }

                    var emailEvent = EmailEvent.EmailEvents.Single(x => x.Code == eventCode);

                    var result = await CreateSubjectAndContentAsync(
                        appToken,
                        applicationServerUrl,
                        emailEvent,
                        Utils.GetString(emailTemplate.Subject),
                        Utils.GetString(emailTemplate.Content),
                        userThatAcceptsTheEmail,
                        userThatTriggeredTheEmail);

                    _emailService.SendMail(new EmailInfo()
                    {
                        MailFromAddress = emailSettings.MailFromAddress,
                        MailFromDisplayName = emailSettings.MailFromDisplayName,
                        MailPassword = emailSettings.MailPassword,
                        MailServer = emailSettings.MailServer,
                        MailServerPort = emailSettings.MailServerPort,
                        MailUserName = emailSettings.MailUserName,
                        Subject = result.Subject,
                        Body = result.Content,
                        Recipients = new string[] { Utils.GetString(userThatAcceptsTheEmail[nameof(Users.Email)]) }
                    });

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// IMPORTANT! Call this before sending an email with the application
        /// </summary>
        public async Task<(string Subject, string Content)> CreateSubjectAndContentAsync(
            string appToken,
            string applicationServerUrl,
            EmailEvent emailEvent,
            string subject,
            string content,
            Dictionary<string, object?> userThatAcceptsTheEmail,
            Dictionary<string, object?> userThatTriggeredTheEmail)
        {
            bool isTheSameUser = Utils.GetLong(userThatAcceptsTheEmail[nameof(Users.ID)]).Equals(Utils.GetLong(userThatTriggeredTheEmail[nameof(Users.ID)]));

            // Fill with user properties

            foreach (var userProperty in EmailEvent.UserProperties)
            {
                string strPlaceholder = $"{{{nameof(Users)}.{userProperty.Key}}}";
                subject = subject.Replace(strPlaceholder, Utils.GetString(userThatAcceptsTheEmail[userProperty.Key]));
                content = content.Replace(strPlaceholder, Utils.GetString(userThatAcceptsTheEmail[userProperty.Key]));

                if (!isTheSameUser)
                {
                    string strPlaceholder_Triggered = $"{{{nameof(Users)}.From.{userProperty.Key}}}";
                    subject = subject.Replace(strPlaceholder_Triggered, Utils.GetString(userThatTriggeredTheEmail[userProperty.Key]));
                    content = content.Replace(strPlaceholder_Triggered, Utils.GetString(userThatTriggeredTheEmail[userProperty.Key]));
                }
            }

            // Fill with global properties

            foreach (var ph in emailEvent.Placeholders)
            {
                string strPlaceholder = $"{{{ph.ToString()}}}";

                switch (ph)
                {
                    case EmailEventsPlaceholders.confirmation_url:
                        {
                            string confirmationToken = Guid.NewGuid().ToString();
                            await _applicationHelperService.CreateEmailConfirmationTokenAsync(appToken, Utils.GetLong(userThatAcceptsTheEmail[nameof(Users.ID)]), confirmationToken);
                            string confirmUrl = $"{applicationServerUrl.Trim('/').ToLower()}/api/Account/Confirm?{Globals.ApplicationTokenQueryParam}={appToken}&token={confirmationToken}";
                            subject = subject.Replace(strPlaceholder, confirmUrl);
                            content = content.Replace(strPlaceholder, confirmUrl);
                        }
                        break;
                    case EmailEventsPlaceholders.reset_password_url:
                        {
                            string resetToken = Guid.NewGuid().ToString();
                            await _applicationHelperService.CreatePasswordResetTokenAsync(appToken, Utils.GetLong(userThatAcceptsTheEmail[nameof(Users.ID)]), resetToken);
                            string resetUrl = $"{applicationServerUrl.Trim('/').ToLower()}/App/{appToken}/Account/Manage/ResetPassword?Token={resetToken}";
                            subject = subject.Replace(strPlaceholder, resetUrl);
                            content = content.Replace(strPlaceholder, resetUrl);
                        }
                        break;
                }
            }

            return (subject, content);
        }
    }
}
