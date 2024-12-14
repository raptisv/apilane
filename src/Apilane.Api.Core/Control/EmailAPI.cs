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
using Apilane.Common.Models.Dto;
using Apilane.Common.Utilities;
using Apilane.Data.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api.Core
{
    public class EmailAPI : IEmailAPI
    {
        private readonly ApiConfiguration _apiConfiguration;
        private readonly IApplicationDataService _appDataService;
        private readonly IApplicationHelperService _applicationHelperService;
        private readonly IApplicationDataStoreFactory _dataStore;
        private readonly IApplicationEmailService _appEmailService;
        private readonly IEmailService _emailService;

        public EmailAPI(
            ApiConfiguration currentConfiguration,
            IApplicationHelperService applicationHelperService,
            IApplicationDataService appDataService,
            IApplicationEmailService appEmailService,
            IEmailService emailService,
            IApplicationDataStoreFactory dataStore)
        {
            _applicationHelperService = applicationHelperService;
            _appEmailService = appEmailService;
            _appDataService = appDataService;
            _apiConfiguration = currentConfiguration;
            _emailService = emailService;
            _dataStore = dataStore;
        }

        public async Task RequestConfirmationAsync(
            DBWS_Application application,
            string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ApilaneException(AppErrors.ERROR, "No email provided");
            }

            if (!Utils.IsValidEmail(email))
            {
                throw new ApilaneException(AppErrors.VALIDATION, "Invalid Email", "Email");
            }

            var userThatAcceptsTheEmail = await GetUserByEmailAsync(application, email);

            // Do not show that we did not find the user
            if (userThatAcceptsTheEmail is not null)
            {
                var isEmailConfirmed = userThatAcceptsTheEmail.TryGetValue(nameof(Users.EmailConfirmed), out var emailConfirmed) &&
                    emailConfirmed is not null &&
                    emailConfirmed is bool boolEmailConfirmed &&
                    boolEmailConfirmed;

                if (!isEmailConfirmed)
                {
                    var userId = userThatAcceptsTheEmail[nameof(Users.ID)];

                    if (userId is null)
                    {
                        throw new Exception("User id is null");
                    }

                    // Do not create new token within the same minute.
                    var emailConfirmationTokensForUserIdExist = await _applicationHelperService
                        .EmailConfirmationTokensForUserIdExistAsync(application.Token, Utils.GetLong(userId), 1);

                    if (emailConfirmationTokensForUserIdExist)
                    {
                        throw new ApilaneException(AppErrors.ERROR, "Too many requests, please try again in a minute.", "Email");
                    }

                    // Send the email
                    await _appEmailService.SendEmailFromApplicationAsync(
                        application.Token,
                        application.Server.ServerUrl,
                        application.GetEmailSettings(),
                        EmailEventsCodes.UserRegisterConfirmation,
                        userThatAcceptsTheEmail,
                        userThatAcceptsTheEmail);
                }
            }
        }

        public async Task ForgotPasswordAsync(
            DBWS_Application application,
            string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ApilaneException(AppErrors.ERROR, $"No email provided");
            }

            if (!Utils.IsValidEmail(email))
            {
                throw new ApilaneException(AppErrors.VALIDATION, "Invalid Email", "Email");
            }

            var userThatAcceptsTheEmail = await GetUserByEmailAsync(application, email);

            // Do not show that we did not find the user
            if (userThatAcceptsTheEmail != null)
            {
                await _appEmailService.SendEmailFromApplicationAsync(
                    application.Token,
                    application.Server.ServerUrl,
                    application.GetEmailSettings(),
                    EmailEventsCodes.UserForgotPassword,
                    userThatAcceptsTheEmail,
                    userThatAcceptsTheEmail);
            }
        }

        public Task<List<EmailTemplateDto>> GetEmailsAsync(string appToken, long? templateId = null)
            => _applicationHelperService.GetEmailsAsync(appToken, templateId);

        public Task UpdateAsync(string appToken, EmailTemplateDto template)
        {
            if (string.IsNullOrWhiteSpace(template.Subject))
            {
                throw new ApilaneException(AppErrors.REQUIRED, null, nameof(template.Subject));
            }

            if (string.IsNullOrWhiteSpace(template.Content))
            {
                throw new ApilaneException(AppErrors.REQUIRED, null, template.Content);
            }

            return _applicationHelperService.UpdateEmailAsync(appToken, template);
        }

        private async Task<Dictionary<string, object?>?> GetUserByEmailAsync(
            DBWS_Application application,
            string userEmail)
        {
            var result = await _dataStore.GetPagedDataAsync(
                nameof(Users),
                null,
                new(nameof(Users.Email), FilterData.FilterOperators.equal, userEmail, PropertyType.String),
                null, 1, 1);

            return ClearUserData(application, result?.Count == 1 ? result.Single() : null);
        }

        private Dictionary<string, object?>? ClearUserData(
            DBWS_Application application,
            Dictionary<string, object?>? drUser)
        {
            if (drUser != null)
            {
                var entity = application.Entities.Single(x => x.Name.Equals(nameof(Users)));

                foreach (var property in entity.Properties.Where(x => x.Encrypted))
                {
                    var propertyValue = drUser[property.Name];
                    if (propertyValue is not null)
                    {
                        string appEncryptionKey = application.EncryptionKey.Decrypt(Globals.EncryptionKey);
                        drUser[property.Name] = Encryptor.Decrypt(propertyValue.ToString(), appEncryptionKey);
                    }
                }

                // IMPORTANT
                drUser[nameof(Users.Password)] = null;
            }

            return drUser;
        }

        public async Task SendAsync(
            string appToken,
            string applicationServerUrl,
            EmailSettings emailSettings,
            long[] userIds,
            int templateId)
        {
            if (userIds == null || userIds.Length == 0)
            {
                throw new ApilaneException(AppErrors.ERROR, $"No recipients found");
            }

            var emailTemplates = await GetEmailsAsync(appToken, templateId);

            if (emailTemplates.Count != 1)
            {
                throw new ApilaneException(AppErrors.NOT_FOUND, $"Template  not found");
            }

            foreach (var id in userIds)
            {
                // IMPORTANT! Parse the template again to reset default content

                var drUserThatAcceptsTheEmail = await _appDataService.GetUserByIdAsync(appToken, id);

                if (drUserThatAcceptsTheEmail != null)
                {
                    var emailEvent = EmailEvent.EmailEvents.Single(x => x.Code.ToString().Equals(emailTemplates[0].EventCode));

                    // IMPORTANT! Call this before sending an email with the application

                    var result = await _appEmailService.CreateSubjectAndContentAsync(
                        appToken,
                        applicationServerUrl,
                        emailEvent,
                        emailTemplates[0].Subject?.ToString()!,
                        emailTemplates[0].Content?.ToString()!,
                        drUserThatAcceptsTheEmail,
                        drUserThatAcceptsTheEmail);

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
                        Recipients = new string[1] { Utils.GetString(drUserThatAcceptsTheEmail[nameof(Users.Email)]) }
                    });
                }
            }
        }
    }
}
