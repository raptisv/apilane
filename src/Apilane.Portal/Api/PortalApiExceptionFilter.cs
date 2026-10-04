using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// Turns exceptions thrown by management API actions into the standard error body.
    /// </summary>
    public class PortalApiExceptionFilter : IExceptionFilter
    {
        private readonly ILogger<PortalApiExceptionFilter> _logger;
        private readonly IWebHostEnvironment _environment;

        public PortalApiExceptionFilter(
            ILogger<PortalApiExceptionFilter> logger,
            IWebHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        public void OnException(ExceptionContext context)
        {
            if (context.Exception is PortalException portalException)
            {
                context.Result = PortalApiErrors.Result(context.HttpContext, portalException);
                context.ExceptionHandled = true;
                return;
            }

            _logger.LogError(context.Exception, "Unhandled exception in {Path}", context.HttpContext.Request.Path);

            // The detail stays in the log; callers get the trace id to find it.
            var message = _environment.IsDevelopment()
                ? context.Exception.Message
                : PortalApiErrors.ErrorMessage;

            context.Result = PortalApiErrors.Result(
                context.HttpContext,
                new PortalException(StatusCodes.Status500InternalServerError, PortalErrorCode.Error, message));
            context.ExceptionHandled = true;
        }
    }
}
