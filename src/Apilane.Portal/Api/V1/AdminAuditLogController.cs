using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/audit-log")]
    public class AdminAuditLogController : PortalAdminApiControllerBase
    {
        private readonly IAuditLogService _auditLogService;

        public AdminAuditLogController(IAuditLogService auditLogService)
        {
            _auditLogService = auditLogService;
        }

        /// <summary>
        /// Returns one page of the instance audit log, newest first: changes to servers, instance
        /// settings and user roles, new applications and database backup downloads. Total counts
        /// every entry, not only the page. Admin only.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ListResponse<AuditLogEntryResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<ListResponse<AuditLogEntryResponse>> List([FromQuery] PageQuery paging)
        {
            // The parameter must not be named 'page': the binder would then look for 'page.Page' and ignore '?Page='.
            return await _auditLogService.QueryAsync(appId: null, paging);
        }
    }
}
