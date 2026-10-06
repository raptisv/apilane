using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    /// <summary>
    /// Cloning an application. For the owner and collaborators; the clone belongs to the caller.
    /// </summary>
    [Route(RoutePrefix + "/clones")]
    public class ApplicationClonesController : PortalApplicationApiControllerBase
    {
        private readonly IApplicationCloneService _applicationCloneService;

        public ApplicationClonesController(IApplicationCloneService applicationCloneService)
        {
            _applicationCloneService = applicationCloneService;
        }

        /// <summary>
        /// Starts a clone of the application on the chosen API server and database and answers at
        /// once; the Location header and OperationId lead to its progress. The clone is a new
        /// application owned by the caller, named '{name} - Clone', with a new token and the same
        /// encryption key, entities, properties, custom endpoints, security and settings. Reports
        /// and collaborators are not copied, files never. With CloneData the records are copied
        /// too: of the entities named in Entities, or of all when that list is left out or empty.
        /// ConnectionString is required unless DatabaseType is SQLLite. Answers 400 VALIDATION for
        /// a value that is not acceptable and for a name in Entities that is not an entity of the
        /// application, and 404 NOT_FOUND (Server) for an unknown ServerID. The API server is
        /// called after this answer: what it refuses (a wrong connection string) shows as a Failed
        /// operation, not as an error here. The new application is listed from this moment on and
        /// stays listed when the operation fails; delete it then. For the owner and collaborators.
        /// </summary>
        [HttpPost]
        [NoAgent]
        [ProducesResponseType(typeof(CloneStartedResponse), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<CloneStartedResponse>> Start(string appToken, CloneApplicationRequest request)
        {
            var started = await _applicationCloneService.StartAsync(appToken, request);

            return Accepted($"/api/v1/applications/{appToken}/clones/{started.OperationId}", started);
        }

        /// <summary>
        /// Returns the progress of a clone: its phase and the counters of that phase. Poll it until
        /// Status is Completed or Failed. Only the user who started the operation can read it, and
        /// only under the application it clones: 404 NOT_FOUND (CloneOperation) otherwise, as for an
        /// unknown id. Operations are kept in the Portal's memory: a completed one can be read for
        /// 30 minutes, and none survives a restart of the Portal.
        /// </summary>
        [HttpGet("{operationId}")]
        [NoAgent]
        [ProducesResponseType(typeof(CloneOperationResponse), StatusCodes.Status200OK)]
        public async Task<CloneOperationResponse> Get(string appToken, string operationId)
        {
            return await _applicationCloneService.GetAsync(appToken, operationId);
        }
    }
}
