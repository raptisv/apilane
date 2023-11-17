using Apilane.Api.Abstractions;
using Apilane.Api.Enums;
using Apilane.Web.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Apilane.Web.Api.Filters
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
        private IQueryDataService _queryService;

        public ApplicationOwnerAuthorizationFilter(
            IQueryDataService queryService)
        {
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
            catch (Exception)
            {
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
