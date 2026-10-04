using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The reports (dashboard panels) of an application the caller owns or collaborates on. A
    /// report is found by its ID inside that application only: an ID of another application is
    /// NOT_FOUND. Reports live in the Portal only: no write calls the API server or resets its cache.
    /// </summary>
    public interface IReportService
    {
        /// <summary>
        /// The reports of the application in dashboard order: by row, then by column.
        /// </summary>
        Task<List<ReportResponse>> GetAllAsync(string appToken);

        /// <summary>
        /// One report of the application.
        /// </summary>
        Task<ReportResponse> GetAsync(string appToken, long reportId);

        /// <summary>
        /// Creates a report as a half-width panel below the existing ones.
        /// </summary>
        Task<ReportResponse> CreateAsync(string appToken, ReportRequest request);

        /// <summary>
        /// Changes the values of a report and replaces its series. The panel stays where it is.
        /// </summary>
        Task<ReportResponse> UpdateAsync(string appToken, long reportId, ReportRequest request);

        /// <summary>
        /// Deletes a report with its series.
        /// </summary>
        Task DeleteAsync(string appToken, long reportId);

        /// <summary>
        /// Moves and resizes the panels listed. Nothing is saved unless every item is acceptable.
        /// </summary>
        Task SaveLayoutAsync(string appToken, ReportLayoutRequest request);

        /// <summary>
        /// What a series of a report of this type can show and be grouped by for the entity.
        /// </summary>
        Task<ReportFieldsResponse> GetFieldsAsync(string appToken, string entityName, string? type);
    }
}
