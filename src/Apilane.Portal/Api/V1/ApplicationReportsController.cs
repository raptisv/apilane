using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    /// <summary>
    /// The reports of an application: the panels of its dashboard. For the owner and
    /// collaborators. A report holds what to ask, not the numbers: the caller runs each series on
    /// the API server (GET {ServerUrl}/api/Stats/Aggregate) with the token of
    /// GET /api/v1/session/api-token. Reports live in the Portal only, so no write here calls the
    /// API server.
    /// </summary>
    [Route(RoutePrefix + "/reports")]
    [AgentPermission("reports")]
    public class ApplicationReportsController : PortalApplicationApiControllerBase
    {
        private readonly IReportService _reportService;

        public ApplicationReportsController(IReportService reportService)
        {
            _reportService = reportService;
        }

        /// <summary>
        /// Lists the reports of the application in dashboard order (by row, then by column), each
        /// with its series resolved against the entities as they are now. A series whose entity
        /// or property no longer exists comes with an Error; the list is still answered.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ListResponse<ReportResponse>), StatusCodes.Status200OK)]
        public async Task<ListResponse<ReportResponse>> List(string appToken)
        {
            return new ListResponse<ReportResponse>(await _reportService.GetAllAsync(appToken));
        }

        /// <summary>
        /// Returns one report. An ID of another application is 404 NOT_FOUND (Report).
        /// </summary>
        [HttpGet("{reportId:long}")]
        [ProducesResponseType(typeof(ReportResponse), StatusCodes.Status200OK)]
        public async Task<ReportResponse> Get(string appToken, long reportId)
        {
            return await _reportService.GetAsync(appToken, reportId);
        }

        /// <summary>
        /// Creates a report. Its panel is 6 columns wide and 4 rows high, at the left below the
        /// existing panels (PUT reports/layout moves it). Answers 400 VALIDATION with Errors naming
        /// each problem by its place (Type, TimeRange, Series[0].Entity, Series[1].Property, ...):
        /// a type or time range that is not known, no series, an entity that does not exist, a
        /// Property or GroupBy that report-fields does not offer for the entity and the type, or
        /// a filter that cannot be read.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(ReportResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<ReportResponse>> Create(string appToken, ReportRequest request)
        {
            var created = await _reportService.CreateAsync(appToken, request);

            return Created($"/api/v1/applications/{Uri.EscapeDataString(appToken)}/reports/{created.ID}", created);
        }

        /// <summary>
        /// Changes the title, type, time range and MaxRecords of a report and replaces its series,
        /// with the rules of create. The panel stays where it is. An ID of another application is
        /// 404 NOT_FOUND (Report).
        /// </summary>
        [HttpPut("{reportId:long}")]
        [ProducesResponseType(typeof(ReportResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<ReportResponse> Update(string appToken, long reportId, ReportRequest request)
        {
            return await _reportService.UpdateAsync(appToken, reportId, request);
        }

        /// <summary>
        /// Deletes a report with its series. This cannot be undone and there is no confirmation
        /// step. The other panels stay where they are. An ID of another application is 404
        /// NOT_FOUND (Report).
        /// </summary>
        [HttpDelete("{reportId:long}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Delete(string appToken, long reportId)
        {
            await _reportService.DeleteAsync(appToken, reportId);

            return NoContent();
        }

        /// <summary>
        /// Moves and resizes panels on the 12-column dashboard grid. Only the reports listed
        /// change. Answers 400 VALIDATION with Errors naming each problem by its place
        /// (Items[0].X, Items[1].ID, ...): a value outside the grid, X + Width above 12, an ID that
        /// is not a report of the application, or a report listed twice. Nothing is saved then.
        /// </summary>
        [HttpPut("layout")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> SaveLayout(string appToken, ReportLayoutRequest request)
        {
            await _reportService.SaveLayoutAsync(appToken, request);

            return NoContent();
        }
    }
}
