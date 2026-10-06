using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    /// <summary>
    /// Sharing an application with other users. Owner only: a collaborator gets 403 FORBIDDEN.
    /// </summary>
    [Route(RoutePrefix + "/collaborators")]
    [NoAgent]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public class CollaboratorsController : PortalApplicationApiControllerBase
    {
        private readonly ICollaboratorService _collaboratorService;

        public CollaboratorsController(ICollaboratorService collaboratorService)
        {
            _collaboratorService = collaboratorService;
        }

        /// <summary>
        /// Lists the users the application is shared with, in the order they were added. Owner only.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ListResponse<CollaboratorResponse>), StatusCodes.Status200OK)]
        public async Task<ListResponse<CollaboratorResponse>> List(string appToken)
        {
            return new ListResponse<CollaboratorResponse>(await _collaboratorService.GetAllAsync(appToken));
        }

        /// <summary>
        /// Lists the agents (users at @agent.local) the application can still be shared with: the
        /// ones that are not collaborators of it yet, by name. Owner only.
        /// </summary>
        [HttpGet("available-agents")]
        [ProducesResponseType(typeof(ListResponse<AvailableAgentResponse>), StatusCodes.Status200OK)]
        public async Task<ListResponse<AvailableAgentResponse>> ListAvailableAgents(string appToken)
        {
            return new ListResponse<AvailableAgentResponse>(await _collaboratorService.GetAvailableAgentsAsync(appToken));
        }

        /// <summary>
        /// Shares the application with an e-mail address. A person gets everything except sharing
        /// it further; an agent gets the supplied permissions, or read-only access when omitted.
        /// The owner can edit an agent's permissions later. The address is trimmed and must be a valid e-mail
        /// address (400 VALIDATION). Answers 409 CONFLICT for the caller's own address and for an
        /// address the application is already shared with, in any letter case. When the instance
        /// mail is configured the address gets a notification mail, unless it is an agent's
        /// (@agent.local); NotificationSent says whether the mail was handed over for sending
        /// (delivery is not reported). The share is matched to an account by exact address, as
        /// typed. Owner only.
        /// </summary>
        [HttpPost]
        [NoAgent]
        [ProducesResponseType(typeof(CollaboratorAddedResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<CollaboratorAddedResponse>> Add(string appToken, AddCollaboratorRequest request)
        {
            var collaborator = await _collaboratorService.AddAsync(appToken, request);

            return StatusCode(StatusCodes.Status201Created, collaborator);
        }

        /// <summary>
        /// Replaces the complete permissions of an agent collaborator. Omitted resources are
        /// denied. Only the application owner may edit access. A human collaborator gives 400
        /// VALIDATION; an id from another application gives 404 NOT_FOUND (Collaborator).
        /// </summary>
        [HttpPut("{id:long}/permissions")]
        [NoAgent]
        [ProducesResponseType(typeof(CollaboratorResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<CollaboratorResponse> UpdatePermissions(string appToken, long id, UpdateAgentPermissionsRequest request)
        {
            return await _collaboratorService.UpdatePermissionsAsync(appToken, id, request);
        }

        /// <summary>
        /// Stops sharing the application with one collaborator. The id must be a collaborator of
        /// this application: 404 NOT_FOUND (Collaborator) otherwise. No mail is sent. Owner only.
        /// </summary>
        [HttpDelete("{id:long}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Delete(string appToken, long id)
        {
            await _collaboratorService.DeleteAsync(appToken, id);

            return NoContent();
        }
    }
}
