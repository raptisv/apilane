using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Enums;
using Apilane.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Apilane.Api.Filters
{
    public class ApplicationOwnerAuthorizeAttribute : TypeFilterAttribute
    {
        public ApplicationOwnerAuthorizeAttribute() 
            : base(typeof(ApplicationOwnerAuthorizationFilter))
        {
            Arguments = new object[] { };
        }
    }

    public class ApplicationOwnerAuthorizationFilter : IAsyncAuthorizationFilter
    {
        private ILogger<ApplicationOwnerAuthorizationFilter> _logger;
        private IQueryDataService _queryService;

        public ApplicationOwnerAuthorizationFilter(
            ILogger<ApplicationOwnerAuthorizationFilter> logger,
            IQueryDataService queryService)
        {
            _logger = logger;
            _queryService = queryService;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            try
            {
                var portalInfoService = context.HttpContext.RequestServices.GetService<IPortalInfoService>()!;
                var userOwnsApplication = _queryService.IsPortalRequest && await portalInfoService.UserOwnsApplicationAsync(_queryService.AuthToken, _queryService.AppToken);
                if (!userOwnsApplication)
                {
                    context.Result = new CustomUnauthorizedResult(AppErrors.UNAUTHORIZED, "Unauthorized");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unauthorized | {ex.Message}");
                context.Result = new CustomUnauthorizedResult(AppErrors.ERROR, "Error");
            }
        }

        public class CustomUnauthorizedResult : JsonResult
        {
            public CustomUnauthorizedResult(AppErrors Error, string Message)
                : base(new { Error = Error.ToString(), Message })
            {
                StatusCode = StatusCodes.Status401Unauthorized;
            }
        }
    }
}
