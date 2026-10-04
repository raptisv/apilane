using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    /// <summary>
    /// Agents: portal users for scripts and AI agents. They are listed with the other users
    /// (GET /api/v1/admin/users); an address that ends with @agent.local is an agent.
    /// </summary>
    [Route(RoutePrefix + "/agents")]
    public class AdminAgentsController : PortalAdminApiControllerBase
    {
        private readonly IUserService _userService;

        public AdminAgentsController(IUserService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// Creates an agent: a portal user with the address {Name}@agent.local and no password,
        /// and a key for it. The agent sends the key as 'Authorization: Bearer {Key}' and is then
        /// that user: it reaches the applications that are shared with its address. The key is in
        /// this answer only; the Portal stores a hash of it. Answers 400 VALIDATION on Name for a
        /// name that is not acceptable or already taken. Admin only.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(AgentCreatedResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<AgentCreatedResponse>> Create(CreateAgentRequest request)
        {
            var agent = await _userService.CreateAgentAsync(request);

            return StatusCode(StatusCodes.Status201Created, agent);
        }

        /// <summary>
        /// Deletes an agent: its user and its key, which stops working at once, and its entries
        /// in the collaborator lists of the applications shared with it. This cannot be undone.
        /// Answers 404 NOT_FOUND for an unknown id and for a user that is not an agent: people
        /// cannot be deleted. Admin only.
        /// </summary>
        [HttpDelete("{userId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(string userId)
        {
            await _userService.DeleteAgentAsync(userId);

            return NoContent();
        }
    }
}
