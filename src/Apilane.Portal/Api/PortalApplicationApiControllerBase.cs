using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// Base class of the endpoints that work on one application. Routes start with
    /// <see cref="RoutePrefix"/>; the application is resolved through IApplicationAccessService,
    /// which answers 404 NOT_FOUND for a token that is unknown or not the caller's (owner or
    /// collaborator; the Admin role gives nothing here). The one exception is POST cache-reset,
    /// which also accepts the Admin role for any application (GetApplicationOrAnyForAdminAsync);
    /// no other action may use that method.
    /// </summary>
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public abstract class PortalApplicationApiControllerBase : PortalApiControllerBase
    {
        public const string RoutePrefix = "api/v1/applications/{appToken}";
    }
}
