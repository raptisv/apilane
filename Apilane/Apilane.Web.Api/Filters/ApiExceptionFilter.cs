using Apilane.Api.Abstractions;
using Apilane.Api.Configuration;
using Apilane.Api.Enums;
using Apilane.Api.Exceptions;
using Apilane.Api.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Utilities;
using Apilane.Web.Api.Models.ViewModels;
using Apilane.Web.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Apilane.Web.Api.Filters
{
    public class ApiExceptionFilter : ExceptionFilterAttribute
    {
        private readonly ILogger<ApiExceptionFilter> _logger;
        private readonly ApiConfiguration _apiConfiguration;

        public ApiExceptionFilter(
            ILogger<ApiExceptionFilter> logger,
            ApiConfiguration apiConfiguration)
        {
            _logger = logger;
            _apiConfiguration = apiConfiguration;
        }

        public override async Task OnExceptionAsync(ExceptionContext context)
        {
            var queryDataService = context.HttpContext.RequestServices.GetService<IQueryDataService>()!;
            var portalInfoService = context.HttpContext.RequestServices.GetService<IPortalInfoService>()!;

            var userHasFullAccess = queryDataService.IsPortalRequest &&!string.IsNullOrWhiteSpace(queryDataService.AuthToken)
                ? await portalInfoService.UserOwnsApplicationAsync(queryDataService.AuthToken, queryDataService.AppToken)
                : false;

            AppErrors error = AppErrors.ERROR;
            string message = EnumProvider<AppErrors>.GetDisplayValue(error);
            string? property = null;
            string? entity = queryDataService.Entity;

            if (string.IsNullOrWhiteSpace(entity) && 
                queryDataService.RouteController.Equals("account", StringComparison.OrdinalIgnoreCase))
            {
                entity = nameof(Users);
            }

            if (context.Exception is ApilaneException apilaneException) 
            {
                error = apilaneException.Error;
                message = apilaneException.CustomMessage ?? apilaneException.Error.ToString();
                property = apilaneException.Property;
                entity = apilaneException.Entity;                
            }
            else
            {
                if (context.Exception is SQLiteException sqliteException &&
                    sqliteException.ResultCode == SQLiteErrorCode.Constraint)
                {
                    property = sqliteException.Message.TryExtractPropertyFromSqlLiteConstraintException(out var appError, out string errorMessage);
                    error = appError;
                    message = errorMessage;
                }
                else if (context.Exception is Microsoft.Data.SqlClient.SqlException sqlServerException &&
                        (sqlServerException.Number == 2627 || sqlServerException.Number == 515 || sqlServerException.Number == 547))
                {
                    property = sqlServerException.Message.TryExtractPropertyFromSqlServerUniqueConstraintException(entity, out var appError, out string errorMessage);
                    error = appError;
                    message = errorMessage;
                }
                else if (context.Exception is MySqlException mySqlException)
                {
                    property = mySqlException.Message.TryExtractPropertyFromMySqlConstraintException(out var appError, out string errorMessage);
                    error = appError;
                    message = errorMessage;
                }
                else
                {
                    if (_apiConfiguration.Environment == HostingEnvironment.Development)
                    {
                        message = context.Exception.Message + (context.Exception.InnerException != null ? Environment.NewLine + "Inner Exception:" + context.Exception.InnerException.Message : string.Empty);
                    }
                }

                using (_logger.BeginScope(new Dictionary<string, object>()
                {
                    ["controller"] = queryDataService.RouteController,
                    ["action"] = queryDataService.RouteAction,
                    ["parameters"] = string.Join(Environment.NewLine, queryDataService.UriParams),
                    ["ip"] = queryDataService.IPAddress,
                }))
                {
                    _logger.Log(
                        logLevel: LogLevel.Error,
                        exception: context.Exception,
                        message: $"API Exception: {GetExceptionFullMessage(context.Exception)}");
                }
            }

            var displayMessage = message ?? EnumProvider<AppErrors>.GetDisplayValue(error);

            if (userHasFullAccess && displayMessage.Equals(Globals.GeneralError))
            {
                // For admins, display a more descriptive error instead of the general error message.
                displayMessage = context.Exception.Message;
            }

            var apiError = new ApiErrorVm()
            {
                Code = error.ToString(),
                Property = property,
                Entity = entity,
                Message = displayMessage
            };

            context.HttpContext.Response.StatusCode = 
                error == AppErrors.UNAUTHORIZED
                ? (int)HttpStatusCode.Unauthorized
                : (int)HttpStatusCode.BadRequest;

            // Always return a JSON result
            context.Result = new JsonResult(apiError);

            await base.OnExceptionAsync(context);
        }

        private static string? GetExceptionFullMessage(Exception ex)
        {            
            return ex is ApilaneException 
                ? null
                : ex.Message + " StackTrace -> " + ex.StackTrace
                + (ex.InnerException != null ? Environment.NewLine + "Inner Exception:" + ex.InnerException.Message + " InnerStackTrace -> " + ex.InnerException.StackTrace : string.Empty);
        }
    }

    public static class ErrorMessageExtensions
    {
        public static string TryExtractPropertyFromSqlLiteConstraintException(
            this string exceptionMessage,
            out AppErrors appError,
            out string errorMessage)
        {
            // The unique contraint ex message will have the following format
            /*
                constraint failed
                UNIQUE constraint failed: {EntityName}.[PropertyName], {EntityName}.[PropertyName]
             */

            // The FK ex message will have the following format
            /*
                 constraint failed
                 FOREIGN KEY constraint failed
             */

            // The not null ex message will have the following format
            /*
                constraint failed
                NOT NULL constraint failed: {EntityName}.[PropertyName]
             */


            appError = AppErrors.ERROR;
            errorMessage = Globals.GeneralError;

            if (exceptionMessage.Contains("UNIQUE constraint"))
            {
                appError = AppErrors.UNIQUE_CONSTRAINT_VIOLATION;
                errorMessage = "Value already exists";
                var listOfProperties = exceptionMessage.Split(':').LastOrDefault()?.Split(',')?.Select(x => x?.Trim()?.Split('.')?.LastOrDefault()?.Trim('[')?.Trim(']'));
                return listOfProperties != null ? string.Join(",", listOfProperties) : string.Empty;
            }
            else if (exceptionMessage.Contains("NOT NULL constraint"))
            {
                appError = AppErrors.REQUIRED;
                errorMessage = "Required";
                var listOfProperties = exceptionMessage.Split(':').LastOrDefault()?.Split(',')?.Select(x => x?.Trim()?.Split('.')?.LastOrDefault()?.Trim('[')?.Trim(']'));
                return listOfProperties != null ? string.Join(",", listOfProperties) : string.Empty;
            }
            else if (exceptionMessage.Contains("FOREIGN KEY constraint"))
            {
                appError = AppErrors.FOREIGN_KEY_CONSTRAINT_VIOLATION;
                errorMessage = "Foreign key constraint violation";
            }

            return string.Empty;
        }

        public static string TryExtractPropertyFromMySqlConstraintException(
            this string exceptionMessage,
            out AppErrors appError,
            out string errorMessage)
        {
            // The unique contraint ex message will have the following format
            /*
                Duplicate entry 'test@test.com' for key 'users.UNIQUE_Users_Email'
             */

            // The FK ex message will have the following format
            /*
                Cannot add or update a child row: a foreign key constraint fails (`local_test`.`testdiff`, 
                CONSTRAINT `FOREIGN_KEY_TestDiff_FKTOMessage_Users` FOREIGN KEY (`FKTOMessage`) 
                REFERENCES `users` (`ID`) ON DELETE CASCADE ON UPDATE CASCADE) 
             */

            // The not null ex message will have the following format
            /*
                Column 'column name' cannot be null
             */

            appError = AppErrors.ERROR;
            errorMessage = Globals.GeneralError;

            if (exceptionMessage.Contains("Duplicate entry"))
            {
                appError = AppErrors.UNIQUE_CONSTRAINT_VIOLATION;
                errorMessage = "Value already exists";
                return exceptionMessage.Trim('\'').Split('_').LastOrDefault() ?? string.Empty;
            }
            else if (exceptionMessage.Contains("Column") &&
                     exceptionMessage.Contains("cannot be null"))
            {
                appError = AppErrors.REQUIRED;
                errorMessage = "Required";
                var messageParts = exceptionMessage.Split('\'');
                if (messageParts.Length == 3)
                {
                    return messageParts[1];
                }
            }
            else if (exceptionMessage.Contains("foreign key constraint fails"))
            {
                appError = AppErrors.FOREIGN_KEY_CONSTRAINT_VIOLATION;
                errorMessage = "Foreign key constraint violation";

                var regex = new Regex("` FOREIGN KEY \\(`(.*)`\\) REFERENCES `");
                var v = regex.Match(exceptionMessage);
                return v?.Groups is not null && v.Groups.Count > 1 
                    ? v.Groups[1].ToString()
                    : string.Empty;
            }

            return string.Empty;
        }

        public static string TryExtractPropertyFromSqlServerUniqueConstraintException(
            this string exceptionMessage,
            string entityName,
            out AppErrors appError,
            out string errorMessage)
        {
            // The unique contraint ex message will have the following format
            /*
                 Violation of UNIQUE KEY constraint 'UNIQUE_TestConstraints_TestConstraints_Str'. Cannot insert duplicate key in object 'dbo.TestConstraints'. The duplicate key value is (1).
                 The statement has been terminated.
            */

            // The FK ex message will have the following format
            /*
                The INSERT statement conflicted with the FOREIGN KEY constraint "FOREIGN_KEY_TestEntity_TestInt_AuthTokens".The conflict occurred in database "TestConstraint", table "dbo.AuthTokens", column 'ID'.
                The statement has been terminated.
             */

            // The not null ex message will have the following format
            /*
                Cannot insert the value NULL into column 'TestInt', table 'TestConstraint.dbo.TestEntity'; column does not allow nulls.INSERT fails.
                The statement has been terminated.
             */


            appError = AppErrors.ERROR;
            errorMessage = Globals.GeneralError;
            
            if (exceptionMessage.Contains("Violation of UNIQUE KEY constraint"))
            {
                appError = AppErrors.UNIQUE_CONSTRAINT_VIOLATION;
                errorMessage = "Value already exists";
                return exceptionMessage.Split('.').FirstOrDefault()?.Trim('\'')?.Split('\'')
                    ?.LastOrDefault()?.Replace($"UNIQUE_{entityName}", string.Empty, System.StringComparison.OrdinalIgnoreCase)
                    ?.Trim('_') ?? string.Empty;
            }
            else if (exceptionMessage.Contains("Cannot insert the value NULL"))
            {
                appError = AppErrors.REQUIRED;
                errorMessage = "Required";
                return exceptionMessage.Split(',').FirstOrDefault()?.Split(' ')?.LastOrDefault()?.Trim('\'') ?? string.Empty;
            }
            else if (exceptionMessage.Contains("FOREIGN KEY constraint"))
            {
                appError = AppErrors.FOREIGN_KEY_CONSTRAINT_VIOLATION;
                errorMessage = "Foreign key constraint violation";
            }

            return string.Empty;
        }
    }
}
