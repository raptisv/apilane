using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/applications")]
    public class AdminApplicationsController : PortalAdminApiControllerBase
    {
        private readonly IAdminApplicationService _adminApplicationService;

        public AdminApplicationsController(IAdminApplicationService adminApplicationService)
        {
            _adminApplicationService = adminApplicationService;
        }

        /// <summary>
        /// Lists every application of the instance, whoever owns it, in the order they were created.
        /// No secret is returned: HasConnectionString and HasMailPassword say whether one is set.
        /// To clear the API-server cache of one of them use POST /api/v1/applications/{appToken}/cache-reset,
        /// which accepts an administrator for any application. Admin only.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ListResponse<AdminApplicationResponse>), StatusCodes.Status200OK)]
        public async Task<ListResponse<AdminApplicationResponse>> List()
        {
            return new ListResponse<AdminApplicationResponse>(await _adminApplicationService.GetAllAsync());
        }

        /// <summary>
        /// Returns any application of the instance with its entities and their properties, for the
        /// administrator's data browser. Its records are read and written on its API server, which
        /// gives an administrator full access to every application. The token is case-sensitive:
        /// 404 NOT_FOUND (Application) for an unknown one. Admin only.
        /// </summary>
        [HttpGet("{appToken}")]
        [ProducesResponseType(typeof(AdminApplicationDetailResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<AdminApplicationDetailResponse> Get(string appToken)
        {
            return await _adminApplicationService.GetAsync(appToken);
        }
    }
}
