using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/entities/{entity}/report-fields")]
    public class EntityReportFieldsController : PortalApplicationApiControllerBase
    {
        private readonly IReportService _reportService;

        public EntityReportFieldsController(IReportService reportService)
        {
            _reportService = reportService;
        }

        /// <summary>
        /// Returns what a series of a report of the given type can show for the entity
        /// (Properties, each with its aggregates) and what it can be grouped by (Groupings). A
        /// report is saved only with values from these lists. For the owner and collaborators.
        /// Type is Grid, Pie, Line, Bar, Radar or StackedBar: 400 VALIDATION (Type) for anything
        /// else. Entity names are case-sensitive: 404 NOT_FOUND (Entity) for any other spelling.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ReportFieldsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<ReportFieldsResponse> Get(string appToken, string entity, [FromQuery(Name = "Type")] string? type)
        {
            return await _reportService.GetFieldsAsync(appToken, entity, type);
        }
    }
}
