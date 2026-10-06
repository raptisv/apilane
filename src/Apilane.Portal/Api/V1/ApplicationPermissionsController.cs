using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/permissions")]
    public class ApplicationPermissionsController : PortalApplicationApiControllerBase
    {
        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IAgentPermissionService _agentPermissionService;

        public ApplicationPermissionsController(
            IApplicationAccessService applicationAccessService,
            IAgentPermissionService agentPermissionService)
        {
            _applicationAccessService = applicationAccessService;
            _agentPermissionService = agentPermissionService;
        }

        /// <summary>
        /// Discovers the caller's current access to this application: effective resource grants,
        /// supported operations and restrictions. Available even when the owner has denied every
        /// resource. Agents should read this before working and refresh it after a 403: owner edits
        /// take effect on the next request. An application not shared with the caller returns 404.
        /// </summary>
        [HttpGet]
        [AgentPermissionDiscovery]
        [ProducesResponseType(typeof(ApplicationPermissionsResponse), StatusCodes.Status200OK)]
        public async Task<ApplicationPermissionsResponse> Get(string appToken)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);
            return await _agentPermissionService.GetPermissionsAsync(application);
        }
    }
}
