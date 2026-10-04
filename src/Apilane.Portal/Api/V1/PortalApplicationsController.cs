using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    // 'Portal' in the name keeps it apart from the MVC ApplicationsController.
    [Route("api/v1/applications")]
    public class PortalApplicationsController : PortalApiControllerBase
    {
        private readonly IApplicationService _applicationService;
        private readonly IApplicationProvisioningService _applicationProvisioningService;

        public PortalApplicationsController(
            IApplicationService applicationService,
            IApplicationProvisioningService applicationProvisioningService)
        {
            _applicationService = applicationService;
            _applicationProvisioningService = applicationProvisioningService;
        }

        /// <summary>
        /// Lists the applications the caller owns and the ones shared with the caller, owned first,
        /// then by name. The Admin role adds nothing to this list.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ListResponse<ApplicationResponse>), StatusCodes.Status200OK)]
        public async Task<ListResponse<ApplicationResponse>> List()
        {
            return new ListResponse<ApplicationResponse>(await _applicationService.GetAllAsync());
        }

        /// <summary>
        /// Creates an application on an API server, owned by the caller. Any signed-in user may do it.
        /// The Portal generates the token and the encryption key. The application starts online, with
        /// the system entities, user registration allowed and one report.
        /// ConnectionString is required unless DatabaseType is SQLLite. Answers 404 for an unknown
        /// ServerID, 400 VALIDATION on ConnectionString with the API server's own message when it
        /// refuses the database, and 502 UPSTREAM_ERROR when the API server cannot be reached or
        /// fails; in both cases nothing is created. A 'Warning' response header means the application
        /// was created and the API server could not be refreshed afterwards.
        /// </summary>
        [HttpPost]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(ApplicationResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<ApplicationResponse>> Create(CreateApplicationRequest request)
        {
            var application = await _applicationProvisioningService.CreateAsync(request);

            return Created(LocationOf(application), application);
        }

        /// <summary>
        /// Creates an application from the application.json of an export, on the chosen API server
        /// and database, owned by the caller. Sent as multipart/form-data. The application keeps the
        /// token and the encryption key of the file; IDs, owner, collaborators and server in the file
        /// are ignored. Answers 400 VALIDATION on File for a missing, empty or unreadable file, 409
        /// CONFLICT when an application with that token exists on this instance, 404 for an unknown
        /// ServerID, 400 VALIDATION on ConnectionString with the API server's own message when it
        /// refuses the database, and 502 UPSTREAM_ERROR when the API server cannot be reached or fails.
        /// </summary>
        [HttpPost("import")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ApplicationResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<ApplicationResponse>> Import([FromForm] ImportApplicationRequest request)
        {
            var application = await _applicationProvisioningService.ImportAsync(request);

            return Created(LocationOf(application), application);
        }

        private static string LocationOf(ApplicationResponse application)
        {
            return $"/api/v1/applications/{application.Token}";
        }
    }
}
