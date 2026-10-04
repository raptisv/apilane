using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// Base class of every management API controller: JSON in and out, a valid portal session
    /// required, the 'X-Apilane-Portal: 1' header required on every request that changes data,
    /// and one error body for every failure.
    /// An action marked [AllowAnonymous] skips the session check only: the header is still
    /// required. Such an action must be on the allow-list of ApiContractTests.
    /// Writes go through ApplicationDbContext.SaveChangesAsync with tracked entities, because
    /// that is where the audit log rows are produced.
    /// </summary>
    [ApiController]
    [Authorize]
    [ServiceFilter(typeof(PortalCsrfFilter), Order = -1)]
    [ServiceFilter(typeof(PortalSessionFilter))]
    [ServiceFilter(typeof(PortalApiExceptionFilter))]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public abstract class PortalApiControllerBase : ControllerBase
    {
    }
}
