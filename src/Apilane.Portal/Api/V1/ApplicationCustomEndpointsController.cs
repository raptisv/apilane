using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    /// <summary>
    /// The custom endpoints of an application: named SQL queries the API server serves at
    /// GET {ServerUrl}/api/Custom/{Name}. For the owner and collaborators. A query is tried out
    /// on the API server, not here: POST {ServerUrl}/api/Custom/TestQuery with the token of
    /// GET /api/v1/session/api-token.
    /// </summary>
    [Route(RoutePrefix + "/custom-endpoints")]
    public class ApplicationCustomEndpointsController : PortalApplicationApiControllerBase
    {
        private readonly ICustomEndpointService _customEndpointService;

        public ApplicationCustomEndpointsController(ICustomEndpointService customEndpointService)
        {
            _customEndpointService = customEndpointService;
        }

        /// <summary>
        /// Lists the custom endpoints of the application in the order they were created, each with
        /// its parameters and its addresses on the API server.
        /// </summary>
        [HttpGet]
        [AgentPermission("custom-endpoints")]
        [ProducesResponseType(typeof(ListResponse<CustomEndpointResponse>), StatusCodes.Status200OK)]
        public async Task<ListResponse<CustomEndpointResponse>> List(string appToken)
        {
            return new ListResponse<CustomEndpointResponse>(await _customEndpointService.GetAllAsync(appToken));
        }

        /// <summary>
        /// Returns one custom endpoint. An ID of another application is 404 NOT_FOUND (CustomEndpoint).
        /// </summary>
        [HttpGet("{id:long}")]
        [AgentPermission("custom-endpoints")]
        [ProducesResponseType(typeof(CustomEndpointResponse), StatusCodes.Status200OK)]
        public async Task<CustomEndpointResponse> Get(string appToken, long id)
        {
            return await _customEndpointService.GetAsync(appToken, id);
        }

        /// <summary>
        /// Creates a custom endpoint. The name is trimmed and must be letters only (400
        /// VALIDATION); 409 CONFLICT when another endpoint of the application has it, whatever its
        /// letter case. Who may call it is set by the security rules of the application. A
        /// 'Warning' response header means the endpoint was created and the API server could not
        /// be refreshed afterwards.
        /// </summary>
        [HttpPost]
        [AgentPermission("custom-endpoints")]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(CustomEndpointResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<CustomEndpointResponse>> Create(string appToken, CustomEndpointRequest request)
        {
            var created = await _customEndpointService.CreateAsync(appToken, request);

            return Created(LocationOf(appToken, created.ID), created);
        }

        /// <summary>
        /// Changes the name, description and query of a custom endpoint, with the rules of create.
        /// Renaming changes its address on the API server, and the security rules written for the
        /// old name no longer apply to it (they are kept, and apply again if the old name comes
        /// back). An ID of another application is 404 NOT_FOUND (CustomEndpoint).
        /// </summary>
        [HttpPut("{id:long}")]
        [AgentPermission("custom-endpoints")]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(CustomEndpointResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        public async Task<CustomEndpointResponse> Update(string appToken, long id, CustomEndpointRequest request)
        {
            return await _customEndpointService.UpdateAsync(appToken, id, request);
        }

        /// <summary>
        /// Deletes a custom endpoint. This cannot be undone and there is no confirmation step.
        /// Security rules written for its name are kept. An ID of another application is 404
        /// NOT_FOUND (CustomEndpoint).
        /// </summary>
        [HttpDelete("{id:long}")]
        [AgentPermission("custom-endpoints")]
        [ProducesCacheResetWarning]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Delete(string appToken, long id)
        {
            await _customEndpointService.DeleteAsync(appToken, id);

            return NoContent();
        }

        /// <summary>
        /// Computes the parameters and the addresses a custom endpoint with this name and query
        /// would have, exactly as a save would. Saves nothing and checks nothing. POST only because
        /// a query does not fit in a URL; it needs the 'X-Apilane-Portal: 1' header like every POST.
        /// </summary>
        [HttpPost("preview")]
        [AgentPermission("custom-endpoints", ReadOnly = true)]
        [ProducesResponseType(typeof(CustomEndpointPreviewResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<CustomEndpointPreviewResponse> Preview(string appToken, CustomEndpointPreviewRequest request)
        {
            return await _customEndpointService.PreviewAsync(appToken, request);
        }

        private static string LocationOf(string appToken, long id)
        {
            return $"/api/v1/applications/{Uri.EscapeDataString(appToken)}/custom-endpoints/{id}";
        }
    }
}
