using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    // Renaming the controller changes the contract: operation ids are built from its name.
    [Route(RoutePrefix)]
    public class PortalApplicationController : PortalApplicationApiControllerBase
    {
        private readonly IApplicationService _applicationService;

        public PortalApplicationController(IApplicationService applicationService)
        {
            _applicationService = applicationService;
        }

        /// <summary>
        /// Returns one application, as the list shows it. For the owner and collaborators.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApplicationResponse), StatusCodes.Status200OK)]
        public async Task<ApplicationResponse> Get(string appToken)
        {
            return await _applicationService.GetAsync(appToken);
        }

        /// <summary>
        /// Returns the application token, the address of its API server and its encryption key.
        /// This is the one place the encryption key is served: no other response carries it.
        /// For the owner and collaborators. Never cached (Cache-Control: no-store).
        /// </summary>
        [HttpGet("connection-info")]
        [NoAgent]
        [ProducesResponseType(typeof(ConnectionInfoResponse), StatusCodes.Status200OK)]
        public async Task<ConnectionInfoResponse> GetConnectionInfo(string appToken)
        {
            return await _applicationService.GetConnectionInfoAsync(appToken);
        }

        /// <summary>
        /// Returns one page of the application's audit log, newest first: the changes made in the
        /// Portal to the application and to its entities, properties, custom endpoints, reports and
        /// collaborators. Secret values read '***'. The entry of the application's creation is in
        /// the instance audit log, not here. Total counts every entry, not only the page. For the
        /// owner and collaborators.
        /// </summary>
        [HttpGet("audit-log")]
        [ProducesResponseType(typeof(ListResponse<AuditLogEntryResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<ListResponse<AuditLogEntryResponse>> GetAuditLog(string appToken, [FromQuery] PageQuery paging)
        {
            // The parameter must not be named 'page': the binder would then look for 'page.Page' and ignore '?Page='.
            return await _applicationService.GetAuditLogAsync(appToken, paging);
        }

        /// <summary>
        /// Makes the API server drop what it has cached about the application and read it again.
        /// For the owner, collaborators and any user with the Admin role. Answers 400 VALIDATION with
        /// the API server's own message when it refuses, and 502 UPSTREAM_ERROR when it cannot be
        /// reached or fails.
        /// </summary>
        [HttpPost("cache-reset")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> ResetCache(string appToken)
        {
            await _applicationService.ResetCacheAsync(appToken);

            return NoContent();
        }

        /// <summary>
        /// Renames the application and sets its connection string. For the owner and collaborators.
        /// The stored database type decides what is written; the database type and the server cannot
        /// be changed. ConnectionString is write-only: null or left out keeps the stored value, a value
        /// replaces it, and it is ignored for an SQLLite application. Answers 400 VALIDATION on
        /// ConnectionString when it is empty, or left out while none is stored, for an application
        /// that is not on SQLLite. A 'Warning' response header means the change was saved and the API
        /// server could not be refreshed afterwards.
        /// </summary>
        [HttpPut]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(ApplicationResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<ApplicationResponse> Update(string appToken, UpdateApplicationRequest request)
        {
            return await _applicationService.UpdateAsync(appToken, request);
        }

        /// <summary>
        /// Takes the application online (Online true) or offline (Online false). Offline, the API
        /// server refuses every call for it. For the owner and collaborators. Sending the current
        /// value changes nothing but still refreshes the API server, which reads the status from
        /// its cache. A 'Warning' response header means the status was saved and the API server
        /// could not be refreshed: it keeps the old status until its cache expires.
        /// </summary>
        [HttpPut("status")]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(ApplicationResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<ApplicationResponse> SetStatus(string appToken, SetApplicationStatusRequest request)
        {
            return await _applicationService.SetStatusAsync(appToken, request);
        }

        /// <summary>
        /// Rebuilds the application on its API server: all its data is dropped and its tables are
        /// created again, empty. The token, the entities and the properties stay. This cannot be
        /// undone and there is no confirmation step. For the owner and collaborators. A refusal of
        /// the API server is 400 VALIDATION with its own message, any other failure of it 502
        /// UPSTREAM_ERROR. A 'Warning' response header means the rebuild was done and the API server
        /// could not be refreshed afterwards.
        /// </summary>
        [HttpPost("rebuild")]
        [NoAgent]
        [ProducesCacheResetWarning]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Rebuild(string appToken)
        {
            await _applicationService.RebuildAsync(appToken);

            return NoContent();
        }

        /// <summary>
        /// Deletes the application: its database and files on the API server, then the application
        /// in the Portal with its entities, properties, custom endpoints, reports and collaborators.
        /// This cannot be undone and there is no confirmation step. For the owner and collaborators.
        /// A refusal of the API server is 400 VALIDATION with its own message, any other failure of
        /// it 502 UPSTREAM_ERROR; in both cases the application stays.
        /// </summary>
        [HttpDelete]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Delete(string appToken)
        {
            await _applicationService.DeleteAsync(appToken);

            return NoContent();
        }
    }
}
