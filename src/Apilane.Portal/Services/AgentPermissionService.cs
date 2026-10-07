using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class AgentPermissionService : IAgentPermissionService
    {
        private static readonly AgentPermissionResource[] Resources =
        {
            new() { Resource = AgentPermissionResources.Application, Name = "Application settings", Description = "Change general settings, application status and reset the cache. Basic application information is always visible; agents cannot delete applications.", CanWrite = true },
            new() { Resource = AgentPermissionResources.Entities, Name = "Entities and properties", Description = "Entities, properties, constraints and default sorting. Agents cannot rename entities or properties. Delete can remove entities and properties with their data.", CanRead = true, CanWrite = true, CanDelete = true },
            new() { Resource = AgentPermissionResources.Security, Name = "Security", Description = "Authentication settings and access rules, including referenced entity, property and endpoint names and types.", CanRead = true, CanWrite = true },
            new() { Resource = AgentPermissionResources.CustomEndpoints, Name = "Custom endpoints", Description = "Custom endpoint definitions and query preview.", CanRead = true, CanWrite = true, CanDelete = true },
            new() { Resource = AgentPermissionResources.Reports, Name = "Reports", Description = "Report definitions, chart settings and dashboard layout, including referenced entity and property names and types.", CanRead = true, CanWrite = true, CanDelete = true },
            new() { Resource = AgentPermissionResources.EmailSettings, Name = "Email settings", Description = "Read application mail configuration. Stored passwords remain hidden; only people can change mail settings.", CanRead = true },
            new() { Resource = AgentPermissionResources.AuditLog, Name = "Audit log", Description = "Historical changes across every application area, including SQL and security rules, regardless of the agent's current grants for those areas. Secret values remain masked.", CanRead = true },
            new() { Resource = AgentPermissionResources.Schema, Name = "Schema comparison and import", Description = "Compare application schemas and import changes. Access to the affected resources is also required.", CanRead = true, CanWrite = true },
            new() { Resource = AgentPermissionResources.Rebuild, Name = "Rebuild application", Description = "Rebuild the application's database and permanently erase its stored data.", CanWrite = true }
        };

        private readonly ApplicationDbContext _dbContext;
        private readonly IPortalAccessService _portalAccessService;
        private readonly IActionDescriptorCollectionProvider _actions;
        private readonly ILogger<AgentPermissionService> _logger;

        public AgentPermissionService(
            ApplicationDbContext dbContext,
            IPortalAccessService portalAccessService,
            IActionDescriptorCollectionProvider actions,
            ILogger<AgentPermissionService> logger)
        {
            _dbContext = dbContext;
            _portalAccessService = portalAccessService;
            _actions = actions;
            _logger = logger;
        }

        public async Task<ApplicationPermissionsResponse> GetPermissionsAsync(DBWS_Application application)
        {
            var user = await _portalAccessService.GetCurrentUserAsync();
            var isAgent = PortalAgent.IsAgent(user.Email);
            var permissions = isAgent ? await GetForCurrentAgentAsync(application, user.Email) : CreateFullPermissions();

            return new ApplicationPermissionsResponse
            {
                IsAgent = isAgent,
                Permissions = permissions,
                Resources = GetResources(),
                Operations = GetOperations(permissions, isAgent, application.UserID == user.Id),
                Restrictions = isAgent ? new List<string>
                {
                    "Agents cannot create, import, clone or delete applications.",
                    "Agents cannot manage collaborators or their permissions.",
                    "Agents cannot access administration endpoints or change constraints of system entities.",
                    "Agents cannot rename entities or properties, regardless of their grants.",
                    "Agents cannot change application mail settings, including SMTP and the email confirmation redirect.",
                    "Agents cannot obtain application encryption keys or Portal API-server tokens.",
                    "Application permissions do not grant API-server access to records, files, record history, statistics, email templates or SQL execution."
                } : new List<string>()
            };
        }

        public async Task<List<AgentPermissionGrant>> GetForCollaboratorAsync(DBWS_Collaborate collaborator)
        {
            var policy = await _dbContext.AgentPermissions
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.CollaborationId == collaborator.ID);

            if (policy is null)
            {
                // Existing shares have no row until their owner saves a policy.
                return CreateReadOnlyPermissions();
            }

            try
            {
                var saved = JsonSerializer.Deserialize<List<AgentPermissionGrant>>(policy.PermissionsJson);
                if (saved is not null)
                {
                    // Mail writes are now human-only. Retire this old grant without invalidating
                    // the rest of an otherwise valid saved policy; new policies still reject it.
                    foreach (var grant in saved.Where(x => x is not null && x.Resource == AgentPermissionResources.EmailSettings && x.Read))
                    {
                        grant.Write = false;
                    }
                }
                return saved is null ? CreateDeniedPermissions() : ValidatePermissions(saved);
            }
            catch (Exception ex) when (ex is JsonException || ex is PortalException)
            {
                // A corrupt stored policy must never restore the read-only default policy.
                _logger.LogError(ex, "Invalid agent permissions for collaboration {CollaborationId}", collaborator.ID);
                return CreateDeniedPermissions();
            }
        }

        public async Task DemandAsync(DBWS_Application application, string resource, AgentPermissionAccess access = AgentPermissionAccess.Read)
        {
            var user = await _portalAccessService.GetCurrentUserAsync();
            if (!PortalAgent.IsAgent(user.Email))
            {
                return;
            }

            var permissions = await GetForCurrentAgentAsync(application, user.Email);
            if (!Allows(permissions, resource, access))
            {
                var operation = access.ToString().ToLowerInvariant();
                throw PortalException.Forbidden($"The agent does not have {operation} access to '{resource}'. Read this application's permissions endpoint for current access.");
            }
        }

        public List<AgentPermissionResource> GetResources()
        {
            return Resources.Select(x => new AgentPermissionResource
            {
                Resource = x.Resource,
                Name = x.Name,
                Description = x.Description,
                CanRead = x.CanRead,
                CanWrite = x.CanWrite,
                CanDelete = x.CanDelete
            }).ToList();
        }

        private async Task<List<AgentPermissionGrant>> GetForCurrentAgentAsync(DBWS_Application application, string? email)
        {
            var collaborator = application.Collaborates.FirstOrDefault(x => string.Equals(x.UserEmail, email, StringComparison.Ordinal));
            return collaborator is null ? CreateDeniedPermissions() : await GetForCollaboratorAsync(collaborator);
        }

        private List<AgentPermissionOperation> GetOperations(List<AgentPermissionGrant> permissions, bool isAgent, bool isOwner)
        {
            var operations = new List<AgentPermissionOperation>();
            var actions = _actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
                .Where(x => typeof(PortalApplicationApiControllerBase).IsAssignableFrom(x.ControllerTypeInfo.AsType()));

            foreach (var action in actions)
            {
                var template = action.AttributeRouteInfo?.Template;
                if (template is null)
                {
                    continue;
                }

                var methods = action.ActionConstraints?.OfType<HttpMethodActionConstraint>()
                    .SelectMany(x => x.HttpMethods).Distinct(StringComparer.Ordinal).ToList() ?? new List<string>();
                foreach (var method in methods)
                {
                    var metadata = action.EndpointMetadata;
                    var humanOnly = metadata.OfType<NoAgentAttribute>().Any();
                    var requirements = humanOnly ? new List<AgentPermissionRequirement>() : metadata.OfType<AgentPermissionAttribute>()
                        .Select(x => new AgentPermissionRequirement { Resource = x.Resource, Access = x.GetAccess(method) }).ToList();
                    var discovery = metadata.OfType<AgentPermissionDiscoveryAttribute>().Any();
                    var allowed = isAgent
                        ? !humanOnly
                            && (discovery || (requirements.Count > 0 && requirements.All(x => Allows(permissions, x.Resource, x.Access))))
                        : isOwner || action.ControllerName != "Collaborators";

                    operations.Add(new AgentPermissionOperation
                    {
                        Method = method,
                        Path = "/" + template.TrimStart('/'),
                        AllowedForThisApplication = allowed,
                        Requirements = requirements,
                        AdditionalRequirements = GetAdditionalRequirements(action.ControllerName, method)
                    });
                }
            }

            return operations.OrderBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Method, StringComparer.Ordinal).ToList();
        }

        private static bool Allows(List<AgentPermissionGrant> permissions, string resource, AgentPermissionAccess access)
        {
            var grant = permissions.FirstOrDefault(x => x.Resource == resource);
            return grant is not null && access switch
            {
                AgentPermissionAccess.Read => grant.Read,
                AgentPermissionAccess.Write => grant.Write,
                AgentPermissionAccess.Delete => grant.Delete,
                _ => false
            };
        }

        private static string? GetAdditionalRequirements(string controller, string method)
        {
            if (controller == "SchemaImport" && method == "POST")
            {
                return "Each nonempty Entities, Security or CustomEndpoints list additionally requires write access to entities, security or custom-endpoints respectively. All permissions are checked before changes begin.";
            }

            if (controller == "ApplicationComparison" || (controller == "SchemaImport" && method == "GET"))
            {
                return "The other application must also be accessible and grant read access to schema, entities, security and custom-endpoints.";
            }

            if (controller == "EntityConstraints" && method == "PUT")
            {
                return "Only a human administrator may change constraints of system entities.";
            }

            return null;
        }

        public List<AgentPermissionGrant> CreateReadOnlyPermissions()
        {
            return Resources.Select(x => new AgentPermissionGrant { Resource = x.Resource, Read = x.CanRead }).ToList();
        }

        public List<AgentPermissionGrant> ValidatePermissions(List<AgentPermissionGrant> permissions)
        {
            var grants = new Dictionary<string, AgentPermissionGrant>(StringComparer.Ordinal);

            foreach (var grant in permissions)
            {
                var resource = grant is null ? null : Resources.FirstOrDefault(x => x.Resource == grant.Resource);
                if (grant is null || resource is null)
                {
                    throw PortalException.Validation("Permissions", "Every permission must name a supported resource.");
                }

                if (!grants.TryAdd(grant.Resource, grant))
                {
                    throw PortalException.Validation("Permissions", $"Resource '{grant.Resource}' is listed more than once.");
                }

                if ((grant.Read && !resource.CanRead) || (grant.Write && !resource.CanWrite) || (grant.Delete && !resource.CanDelete))
                {
                    throw PortalException.Validation("Permissions", $"Resource '{grant.Resource}' does not support the requested operation.");
                }

                if (resource.CanRead && (grant.Write || grant.Delete) && !grant.Read)
                {
                    throw PortalException.Validation("Permissions", $"Write and delete access to '{grant.Resource}' require read access.");
                }
            }

            return Resources.Select(resource => grants.TryGetValue(resource.Resource, out var grant)
                ? new AgentPermissionGrant { Resource = grant.Resource, Read = grant.Read, Write = grant.Write, Delete = grant.Delete }
                : new AgentPermissionGrant { Resource = resource.Resource }).ToList();
        }

        private static List<AgentPermissionGrant> CreateDeniedPermissions()
        {
            return Resources.Select(x => new AgentPermissionGrant { Resource = x.Resource }).ToList();
        }

        private static List<AgentPermissionGrant> CreateFullPermissions()
        {
            return Resources.Select(x => new AgentPermissionGrant { Resource = x.Resource, Read = x.CanRead, Write = x.CanWrite, Delete = x.CanDelete }).ToList();
        }
    }
}
