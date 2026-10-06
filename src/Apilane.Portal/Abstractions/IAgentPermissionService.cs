using Apilane.Common.Models;
using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Resolves and validates application permissions for agents. Application visibility and
    /// owner checks stay with <see cref="IApplicationAccessService"/>.
    /// </summary>
    public interface IAgentPermissionService
    {
        Task<ApplicationPermissionsResponse> GetPermissionsAsync(DBWS_Application application);

        Task<List<AgentPermissionGrant>> GetForCollaboratorAsync(DBWS_Collaborate collaborator);

        Task DemandAsync(DBWS_Application application, string resource, AgentPermissionAccess access = AgentPermissionAccess.Read);

        List<AgentPermissionResource> GetResources();

        List<AgentPermissionGrant> CreateReadOnlyPermissions();

        List<AgentPermissionGrant> ValidatePermissions(List<AgentPermissionGrant> permissions);
    }
}
