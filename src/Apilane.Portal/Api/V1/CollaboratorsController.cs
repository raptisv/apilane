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
        /// Shares the application with an e-mail address, which gets administrator access to it:
        /// everything except sharing it further. The address is trimmed and must be a valid e-mail
        /// address (400 VALIDATION). Answers 409 CONFLICT for the caller's own address and for an
        /// address the application is already shared with, in any letter case. When the instance
        /// mail is configured the address gets a notification mail; NotificationSent says whether
        /// the mail was handed over for sending (delivery is not reported). The share is matched
        /// to an account by exact address, as typed. Owner only.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(CollaboratorAddedResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<CollaboratorAddedResponse>> Add(string appToken, AddCollaboratorRequest request)
        {
            var collaborator = await _collaboratorService.AddAsync(appToken, request);

            return StatusCode(StatusCodes.Status201Created, collaborator);
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
