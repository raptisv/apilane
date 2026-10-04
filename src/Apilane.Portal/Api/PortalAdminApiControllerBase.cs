using Apilane.Common;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// Base class of the instance administration endpoints: the caller must hold the Admin role.
    /// Routes start with <see cref="RoutePrefix"/>.
    /// </summary>
    [Authorize(Roles = Globals.AdminRoleName)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public abstract class PortalAdminApiControllerBase : PortalApiControllerBase
    {
        public const string RoutePrefix = "api/v1/admin";
    }
}
