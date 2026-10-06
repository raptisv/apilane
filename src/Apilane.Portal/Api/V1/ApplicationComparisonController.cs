using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/comparison")]
    [AgentPermission("schema")]
    [AgentPermission("entities")]
    [AgentPermission("security")]
    [AgentPermission("custom-endpoints")]
    public class ApplicationComparisonController : PortalApplicationApiControllerBase
    {
        private readonly IApplicationComparisonService _applicationComparisonService;

        public ApplicationComparisonController(IApplicationComparisonService applicationComparisonService)
        {
            _applicationComparisonService = applicationComparisonService;
        }

        /// <summary>
        /// Compares this application (the source) with the Target application: for entities,
        /// custom endpoints and security rules, what only the target has (Added), what only this
        /// one has (Removed) and what both have with different values (Changed, with the value
        /// before, in this application, and after, in the target). Entities, custom endpoints and
        /// security rules are matched by their exact name: 'Orders' and 'orders' are different.
        /// Inside an entity both applications have, only custom properties and custom constraints
        /// are compared. Security rules of an entity or custom endpoint that no longer exists, and
        /// Schema rules, are not compared. Reports, settings and data are not compared. Every list
        /// is empty when the applications are identical. For the owner and collaborators of both
        /// applications: answers 404 NOT_FOUND when the caller cannot see the target, 400
        /// VALIDATION on Target when it is this application, and 409 CONFLICT when the stored
        /// security rules of either application cannot be read. Nothing is changed and the API
        /// server is not called.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApplicationComparisonResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        public async Task<ApplicationComparisonResponse> Get(
            string appToken,
            [FromQuery(Name = "Target")][Required(ErrorMessage = "Required")] string target)
        {
            return await _applicationComparisonService.CompareAsync(appToken, target);
        }
    }
}
