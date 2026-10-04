using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/servers")]
    public class AdminServersController : PortalAdminApiControllerBase
    {
        private readonly IServerService _serverService;

        public AdminServersController(IServerService serverService)
        {
            _serverService = serverService;
        }

        /// <summary>
        /// Lists the API servers registered on this instance, in the order they were added. Admin only.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ListResponse<ServerResponse>), StatusCodes.Status200OK)]
        public async Task<ListResponse<ServerResponse>> List()
        {
            return new ListResponse<ServerResponse>(await _serverService.GetAllAsync());
        }

        /// <summary>
        /// Returns one API server. Admin only.
        /// </summary>
        [HttpGet("{id:long}")]
        [ProducesResponseType(typeof(ServerResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<ServerResponse> Get(long id)
        {
            return await _serverService.GetAsync(id);
        }

        /// <summary>
        /// Registers an API server. Name and ServerUrl are required and ServerUrl must be an absolute http or https URL. Admin only.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(ServerResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<ServerResponse>> Create(ServerRequest request)
        {
            var server = await _serverService.CreateAsync(request);

            return CreatedAtAction(nameof(Get), new { id = server.ID }, server);
        }

        /// <summary>
        /// Changes the name and the URL of an API server. A new URL takes effect at once for every
        /// application on the server. Admin only.
        /// </summary>
        [HttpPut("{id:long}")]
        [ProducesResponseType(typeof(ServerResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<ServerResponse> Update(long id, ServerRequest request)
        {
            return await _serverService.UpdateAsync(id, request);
        }

        /// <summary>
        /// Removes an API server. Answers 409 CONFLICT while applications are hosted on it. Admin only.
        /// </summary>
        [HttpDelete("{id:long}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Delete(long id)
        {
            await _serverService.DeleteAsync(id);

            return NoContent();
        }
    }
}
