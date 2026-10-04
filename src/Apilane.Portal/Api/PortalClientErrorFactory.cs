using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// Gives status-only results of the management API (a bare NotFound(), the 415 for a wrong
    /// Content-Type) the standard error body instead of the framework's ProblemDetails.
    /// </summary>
    public class PortalClientErrorFactory : IClientErrorFactory
    {
        public IActionResult GetClientError(ActionContext actionContext, IClientErrorActionResult clientError)
        {
            var statusCode = clientError.StatusCode ?? StatusCodes.Status400BadRequest;

            var (code, message) = statusCode switch
            {
                StatusCodes.Status400BadRequest => (PortalErrorCode.Validation, PortalApiErrors.ValidationMessage),
                StatusCodes.Status401Unauthorized => (PortalErrorCode.Unauthorized, PortalApiErrors.UnauthorizedMessage),
                StatusCodes.Status403Forbidden => (PortalErrorCode.Forbidden, PortalApiErrors.ForbiddenMessage),
                StatusCodes.Status404NotFound => (PortalErrorCode.NotFound, "Not found."),
                StatusCodes.Status409Conflict => (PortalErrorCode.Conflict, "The request conflicts with the current state."),
                StatusCodes.Status415UnsupportedMediaType => (PortalErrorCode.Error, "Send the request body as application/json."),
                _ => (PortalErrorCode.Error, "The request could not be processed.")
            };

            return new ObjectResult(PortalApiErrors.Create(actionContext.HttpContext, code, message))
            {
                StatusCode = statusCode
            };
        }
    }
}
