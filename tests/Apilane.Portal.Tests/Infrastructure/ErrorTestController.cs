using Apilane.Portal.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Tests.Infrastructure
{
    /// <summary>
    /// Actions that fail in each way the shared error pipeline has to handle. Routed under
    /// api/test, so it is not part of the api/v1 contract.
    /// </summary>
    [Route("api/test/errors")]
    public class ErrorTestController : PortalApiControllerBase
    {
        public const string ExceptionMessage = "The test action failed on purpose.";

        [HttpGet("exception")]
        public IActionResult ThrowException()
        {
            throw new InvalidOperationException(ExceptionMessage);
        }

        [HttpGet("portal-exception")]
        public IActionResult ThrowPortalException()
        {
            throw new PortalException(StatusCodes.Status409Conflict, PortalErrorCode.Conflict, "Taken.", entity: "Thing", property: "Name");
        }

        [HttpGet("conflict")]
        public IActionResult ThrowConflict()
        {
            throw PortalException.Conflict("In use.", "Thing");
        }

        [HttpGet("forbidden")]
        public IActionResult ThrowForbidden()
        {
            throw PortalException.Forbidden();
        }

        [HttpGet("validation")]
        public IActionResult ThrowValidation()
        {
            throw PortalException.Validation("Name", "Already used.");
        }

        [HttpGet("bare-not-found")]
        public IActionResult BareNotFound()
        {
            return NotFound();
        }

        [HttpPost("body")]
        public IActionResult Body(ErrorTestBody body)
        {
            return Ok(body);
        }

        [HttpPost("anonymous")]
        [AllowAnonymous]
        public IActionResult Anonymous()
        {
            return NoContent();
        }
    }

    public class ErrorTestBody
    {
        [Required]
        public string Name { get; set; } = string.Empty;
    }
}
