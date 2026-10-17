using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Services;
using Apilane.Common;
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
            // Exception filters do not handle what an authorization filter throws, so the error body
            // (the one ApiExceptionFilter writes for every other endpoint) is written here.
            try
            {
                var portalInfoService = context.HttpContext.RequestServices.GetService<IPortalInfoService>()!;
                var userOwnsApplication = _queryService.IsPortalRequest && await portalInfoService.UserOwnsApplicationAsync(_queryService.AuthToken, _queryService.AppToken);
                if (!userOwnsApplication)
                {
                    context.Result = ApiExceptionFilter.ErrorResult(AppErrors.UNAUTHORIZED, "Unauthorized", null, null);
                    return;
                }
            }
            catch (ApilaneException ex)
            {
                // For example the Portal could not be reached
                context.Result = ApiExceptionFilter.ErrorResult(ex.Error, ex.CustomMessage ?? ex.Error.ToString(), ex.Property, ex.Entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unauthorized | {ex.Message}");
                context.Result = ApiExceptionFilter.ErrorResult(AppErrors.ERROR, Globals.GeneralError, null, null);
            }
        }
    }
}
