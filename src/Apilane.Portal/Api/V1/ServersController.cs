using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route("api/v1/servers")]
    public class ServersController : PortalApiControllerBase
    {
        private readonly IServerService _serverService;

        public ServersController(IServerService serverService)
        {
            _serverService = serverService;
        }

        /// <summary>
        /// Lists the API servers of this instance by name, for any signed-in user: the servers an
        /// application can be created on. Managing servers is under /api/v1/admin/servers.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ListResponse<ServerSummaryResponse>), StatusCodes.Status200OK)]
        public async Task<ListResponse<ServerSummaryResponse>> List()
        {
            return new ListResponse<ServerSummaryResponse>(await _serverService.GetSummariesAsync());
        }
    }
}
