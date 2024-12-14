using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Common.Utilities;
using Apilane.Api.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace Apilane.Api.Filters
{
    public class ApplicationLogActionFilter : IAsyncActionFilter
    {
        private readonly ILogger<ApplicationLogActionFilter> _logger;

        public ApplicationLogActionFilter(
            ILogger<ApplicationLogActionFilter> logger)
        {
            _logger = logger;
        }

        private static Exception? CleanupException(Exception? exception)
        {
            // The below exceptions are considered handled
            return exception switch
            {
                ApilaneException => null,
                SQLiteException sqliteException when sqliteException.ResultCode == SQLiteErrorCode.Constraint => null,
                SqlException sqlServerException when sqlServerException.Number == 2627 => null,
                _ => exception
            };
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext actionContext, ActionExecutionDelegate next)
        {
            // Start watch

            var sw = Stopwatch.StartNew();

            // Do things

            var executedContext = await next();

            // Log

            var queryService = actionContext.HttpContext.RequestServices.GetService<IQueryDataService>()!;

            // Calculate badwidth consumed

            var entityOrEndpoint = !string.IsNullOrWhiteSpace(queryService.Entity) ? queryService.Entity : queryService.CustomEndpoint;

            var (response, statusCode) = FormatResponse(executedContext.Exception);

            var exception = CleanupException(executedContext.Exception);

            var logLevel = exception is null
                ? LogLevel.Information
                : LogLevel.Error;

            var logMessageItems = new List<string>()
            {
                ((int)statusCode).ToString(),
                $"{queryService.RouteController}-{queryService.RouteAction}-{entityOrEndpoint}"
            };

            if (exception is not null)
            {
                logMessageItems.Add($"Error: {exception.Message}");
            }

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["apptoken"] = queryService.AppToken,
                ["controller"] = queryService.RouteController,
                ["action"] = queryService.RouteAction,
                ["parameters"] = string.Join(Environment.NewLine, queryService.UriParams),
                ["entity"] = entityOrEndpoint,
                ["ip"] = queryService.IPAddress,
                ["status_code"] = (int)statusCode,
                ["timetaken"] = sw.ElapsedMilliseconds,
                ["response"] = response
            }))
            {
                _logger.Log(
                    logLevel: logLevel,
                    exception: exception,
                    message: string.Join(" | ", logMessageItems));
            }

            sw.Stop();
        }

        private static (object Response, HttpStatusCode StatusCode) FormatResponse(Exception? exception)
        {
            object response = "...";
            var statusCode = HttpStatusCode.OK;

            if (exception is not null)
            {
                if (exception is ApilaneException apilaneException)
                {
                    statusCode = apilaneException.Error == AppErrors.UNAUTHORIZED ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest;

                    response = new
                    {
                        Code = apilaneException.Error.ToString(),
                        Property = apilaneException.Property,
                        Message = (apilaneException.CustomMessage ?? EnumProvider<AppErrors>.GetDisplayValue(apilaneException.Error))
                    };
                }
                else
                {
                    statusCode = HttpStatusCode.BadRequest;
                }
            }

            return (response, statusCode);
        }
    }
}
