using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
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
        private readonly IAgentPermissionService _agentPermissionService;
        private readonly ILogger<CollaboratorService> _logger;

        public CollaboratorService(
            ApplicationDbContext dbContext,
            IApplicationAccessService applicationAccessService,
            IPortalAccessService portalAccessService,
            IPortalSettingsService portalSettingsService,
            IPortalMailService portalMailService,
            IPortalLinkBuilder portalLinkBuilder,
            IAgentPermissionService agentPermissionService,
            ILogger<CollaboratorService> logger)
        {
            _dbContext = dbContext;
            _applicationAccessService = applicationAccessService;
            _portalAccessService = portalAccessService;
            _portalSettingsService = portalSettingsService;
            _portalMailService = portalMailService;
            _portalLinkBuilder = portalLinkBuilder;
            _agentPermissionService = agentPermissionService;
            _logger = logger;
        }

        public async Task<List<CollaboratorResponse>> GetAllAsync(string appToken)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken, requireOwner: true);

            var collaborators = new List<CollaboratorResponse>();
            foreach (var collaborator in application.Collaborates.OrderBy(x => x.ID))
            {
                collaborators.Add(await ToResponseAsync(collaborator));
            }

            return collaborators;
        }

        public async Task<List<AvailableAgentResponse>> GetAvailableAgentsAsync(string appToken)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken, requireOwner: true);

            // Identity keeps every address in upper case too, so the letter case does not matter here.
            var suffix = PortalAgent.EmailSuffix.ToUpperInvariant();

            var emails = await _dbContext.Users
                .AsNoTracking()
                .Where(x => x.Email != null && x.NormalizedEmail != null && x.NormalizedEmail.EndsWith(suffix))
                .Select(x => x.Email ?? string.Empty)
                .ToListAsync();

            return emails
                .Where(x => PortalAgent.IsAgent(x) && !application.Collaborates.Any(c => c.UserEmail.Equals(x, StringComparison.OrdinalIgnoreCase)))
                .Select(x => new AvailableAgentResponse { Name = x.Substring(0, x.Length - PortalAgent.EmailSuffix.Length), Email = x })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
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

            var isAgent = PortalAgent.IsAgent(email);
            if (!isAgent && request.Permissions is not null)
            {
                throw PortalException.Validation(nameof(request.Permissions), "Permissions can only be set for agent collaborators.");
            }

            var permissions = isAgent
                ? request.Permissions is null
                    ? _agentPermissionService.CreateReadOnlyPermissions()
                    : _agentPermissionService.ValidatePermissions(request.Permissions)
                : null;

            var collaborator = new DBWS_Collaborate
            {
                AppID = application.ID,
                UserEmail = email,
                DateModified = DateTime.UtcNow
            };

            _dbContext.Collaborations.Add(collaborator);
            if (permissions is not null)
            {
                // EF inserts the collaboration and its policy together, propagating the generated
                // collaboration ID through the relationship in the same SaveChanges transaction.
                _dbContext.AgentPermissions.Add(new PortalAgentPermission
                {
                    Collaboration = collaborator,
                    PermissionsJson = JsonSerializer.Serialize(permissions)
                });
            }

            await _dbContext.SaveChangesAsync();

            return new CollaboratorAddedResponse
            {
                ID = collaborator.ID,
                Email = collaborator.UserEmail,
                Permissions = permissions,
                // An agent is never mailed: nobody reads its address.
                NotificationSent = !isAgent && SendNotification(application, email)
            };
        }

        public async Task<CollaboratorResponse> UpdatePermissionsAsync(string appToken, long id, UpdateAgentPermissionsRequest request)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken, requireOwner: true);
            var collaborator = application.Collaborates.FirstOrDefault(x => x.ID == id)
                ?? throw PortalException.NotFound(EntityName);

            if (!PortalAgent.IsAgent(collaborator.UserEmail))
            {
                throw PortalException.Validation(nameof(request.Permissions), "Permissions can only be set for agent collaborators.");
            }

            var permissions = _agentPermissionService.ValidatePermissions(request.Permissions);
            var policy = await _dbContext.AgentPermissions.SingleOrDefaultAsync(x => x.CollaborationId == id);
            if (policy is null)
            {
                policy = new PortalAgentPermission { Collaboration = collaborator };
                _dbContext.AgentPermissions.Add(policy);
            }

            policy.PermissionsJson = JsonSerializer.Serialize(permissions);
            await _dbContext.SaveChangesAsync();

            return new CollaboratorResponse
            {
                ID = collaborator.ID,
                Email = collaborator.UserEmail,
                Permissions = permissions
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

        private async Task<CollaboratorResponse> ToResponseAsync(DBWS_Collaborate collaborator)
        {
            return new CollaboratorResponse
            {
                ID = collaborator.ID,
                Email = collaborator.UserEmail,
                Permissions = PortalAgent.IsAgent(collaborator.UserEmail)
                    ? await _agentPermissionService.GetForCollaboratorAsync(collaborator)
                    : null
            };
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
