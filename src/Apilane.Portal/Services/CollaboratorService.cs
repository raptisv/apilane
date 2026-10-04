using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class CollaboratorService : ICollaboratorService
    {
        public const string InvalidEmailMessage = "Invalid email address";
        public const string AlreadySharedMessage = "Already shared with that user";
        public const string SelfShareMessage = "You cannot share an application with yourself";

        private const string EntityName = "Collaborator";

        private readonly ApplicationDbContext _dbContext;
        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IPortalAccessService _portalAccessService;
        private readonly IPortalSettingsService _portalSettingsService;
        private readonly IPortalMailService _portalMailService;
        private readonly IPortalLinkBuilder _portalLinkBuilder;
        private readonly ILogger<CollaboratorService> _logger;

        public CollaboratorService(
            ApplicationDbContext dbContext,
            IApplicationAccessService applicationAccessService,
            IPortalAccessService portalAccessService,
            IPortalSettingsService portalSettingsService,
            IPortalMailService portalMailService,
            IPortalLinkBuilder portalLinkBuilder,
            ILogger<CollaboratorService> logger)
        {
            _dbContext = dbContext;
            _applicationAccessService = applicationAccessService;
            _portalAccessService = portalAccessService;
            _portalSettingsService = portalSettingsService;
            _portalMailService = portalMailService;
            _portalLinkBuilder = portalLinkBuilder;
            _logger = logger;
        }

        public async Task<List<CollaboratorResponse>> GetAllAsync(string appToken)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken, requireOwner: true);

            return application.Collaborates
                .OrderBy(x => x.ID)
                .Select(x => new CollaboratorResponse { ID = x.ID, Email = x.UserEmail })
                .ToList();
        }

        public async Task<CollaboratorAddedResponse> AddAsync(string appToken, AddCollaboratorRequest request)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken, requireOwner: true);
            var owner = await _portalAccessService.GetCurrentUserAsync();

            var email = Utils.GetString(request.Email);

            if (!Utils.IsValidEmail(email))
            {
                throw PortalException.Validation(nameof(AddCollaboratorRequest.Email), InvalidEmailMessage);
            }

            if (string.Equals(owner.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                throw PortalException.Conflict(SelfShareMessage, EntityName);
            }

            if (application.Collaborates.Any(x => x.UserEmail.Equals(email, StringComparison.OrdinalIgnoreCase)))
            {
                throw PortalException.Conflict(AlreadySharedMessage, EntityName);
            }

            var collaborator = new DBWS_Collaborate
            {
                AppID = application.ID,
                UserEmail = email,
                DateModified = DateTime.UtcNow
            };

            _dbContext.Collaborations.Add(collaborator);
            await _dbContext.SaveChangesAsync();

            return new CollaboratorAddedResponse
            {
                ID = collaborator.ID,
                Email = collaborator.UserEmail,
                NotificationSent = SendNotification(application, email)
            };
        }

        public async Task DeleteAsync(string appToken, long id)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken, requireOwner: true);

            // Only a collaborator of this application: an id of another application is not found.
            var collaborator = application.Collaborates.FirstOrDefault(x => x.ID == id)
                ?? throw PortalException.NotFound(EntityName);

            _dbContext.Collaborations.Remove(collaborator);
            await _dbContext.SaveChangesAsync();
        }

        // Tells the new collaborator by mail, with a link to the applications page (/apps).
        // The share is saved already, so a mail that cannot be handed over is reported as
        // NotificationSent = false instead of a 500. SMTP errors are logged by EmailService itself.
        private bool SendNotification(DBWS_Application application, string recipient)
        {
            try
            {
                var settings = _portalSettingsService.Get();

                return _portalMailService.Send(
                    recipient,
                    $"{settings.InstanceTitle} - admin rights to {application.Name}",
                    $"User {WebUtility.HtmlEncode(application.AdminEmail)} shared administrator rights to application <b>{WebUtility.HtmlEncode(application.Name)}</b> with you.<br/>" +
                    $"Navigate to the <a href='{_portalLinkBuilder.Ui("apps")}'>portal</a> to access the application.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The share notification for application {AppToken} could not be sent", application.Token);
                return false;
            }
        }
    }
}
