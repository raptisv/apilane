using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Utilities;
using Apilane.Api.Extensions;
using Apilane.Api.Models.ViewModels;
using Apilane.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Apilane.Api.Filters
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
                else if (context.Exception is SqlException sqlServerException)
                {
                    property = sqlServerException.TryExtractPropertyFromSqlServerConstraintException(entity, out var appError, out string errorMessage);
                    error = appError;
                    message = errorMessage;
                }
                else if (context.Exception is MySqlException mySqlException)
                {
                    property = mySqlException.Message.TryExtractPropertyFromMySqlConstraintException(out var appError, out string errorMessage);
                    error = appError;
                    message = errorMessage;
                }
                else if (context.Exception is PostgresException postgresException)
                {
                    property = postgresException.TryExtractPropertyFromPostgresConstraintException(out var appError, out string errorMessage);
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
                    ["parameters"] = string.Join(Environment.NewLine, queryDataService.GetUriParamsForLog()),
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

            if (userHasFullAccess && displayMessage.Equals(Globals.GeneralError) && context.Exception is not SqlException)
            {
                // SQL errors may contain stored record values, including when a Portal agent calls
                // a management operation. Never replace their sanitized result with driver text.
                // Preserve the existing diagnostics for other privileged errors.
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

        public static string TryExtractPropertyFromSqlServerConstraintException(
            this SqlException exception,
            string? entityName,
            out AppErrors appError,
            out string errorMessage)
        {
            appError = AppErrors.ERROR;
            errorMessage = Globals.GeneralError;

            // A failed index build can report 1750 or a statement-termination error before 1505.
            // Inspect the full collection; the combined Message also contains duplicate values.
            var errors = exception.Errors.Cast<SqlError>().ToList();
            if (errors.Any(error => error.Number == 1505))
            {
                appError = AppErrors.UNIQUE_CONSTRAINT_VIOLATION;
                errorMessage = "Cannot create a unique constraint because duplicate values exist. Remove duplicate values and try again.";
                return string.Empty;
            }

            var constraintError = errors.FirstOrDefault(error => error.Number is 2601 or 2627 or 515 or 547);
            if (constraintError is null)
            {
                return string.Empty;
            }

            switch (constraintError.Number)
            {
                case 2601:
                case 2627:
                {
                    appError = AppErrors.UNIQUE_CONSTRAINT_VIOLATION;
                    errorMessage = "Value already exists";

                    // Parse only the constraint name at the start of this one error, never the
                    // duplicate value or text appended by another error in the same batch.
                    var match = Regex.Match(constraintError.Message, "\\AViolation of UNIQUE KEY constraint '(?<name>[^']+)'\\.");
                    var constraintName = match.Groups["name"].Value;
                    var prefix = $"UNIQUE_{entityName}_";
                    return !string.IsNullOrEmpty(entityName) && constraintName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        ? constraintName.Substring(prefix.Length)
                        : string.Empty;
                }

                case 515:
                {
                    appError = AppErrors.REQUIRED;
                    errorMessage = "Required";
                    return Regex.Match(constraintError.Message, "\\ACannot insert the value NULL into column '(?<name>[^']+)'").Groups["name"].Value;
                }

                case 547 when constraintError.Message.Contains("FOREIGN KEY constraint", StringComparison.Ordinal):
                {
                    appError = AppErrors.FOREIGN_KEY_CONSTRAINT_VIOLATION;
                    errorMessage = "Foreign key constraint violation";
                    break;
                }
            }

            return string.Empty;
        }

        public static string? TryExtractPropertyFromPostgresConstraintException(
            this PostgresException ex,
            out AppErrors appError,
            out string errorMessage)
        {
            // SqlState codes (class 23 — Integrity Constraint Violation):
            //   23505  unique_violation
            //   23502  not_null_violation
            //   23503  foreign_key_violation

            // Unique violation — ConstraintName follows our naming convention:
            //   UNIQUE_{EntityName}_{Col1}_{Col2}
            // e.g. UNIQUE_Users_Email  →  property = "Email"

            // Not-null violation — ColumnName is populated directly by PostgreSQL.

            // Foreign key violation — ConstraintName follows our naming convention:
            //   FOREIGN_KEY_{EntityName}_{Property}_{FKEntity}
            // e.g. FOREIGN_KEY_Orders_UserId_Users  →  property = "UserId"

            appError = AppErrors.ERROR;
            errorMessage = Globals.GeneralError;

            switch (ex.SqlState)
            {
                case PostgresErrorCodes.UniqueViolation:
                {
                    appError = AppErrors.UNIQUE_CONSTRAINT_VIOLATION;
                    errorMessage = "Value already exists";

                    // ConstraintName = "UNIQUE_{EntityName}_{Col1}_{Col2}..."
                    // Strip the "UNIQUE_" prefix and the entity-name segment, return the rest.
                    var constraintName = ex.ConstraintName;
                    if (!string.IsNullOrWhiteSpace(constraintName) &&
                        constraintName.StartsWith("UNIQUE_", StringComparison.OrdinalIgnoreCase))
                    {
                        // Parts: ["UNIQUE", EntityName, Col1, Col2, ...]
                        var parts = constraintName.Split('_');
                        if (parts.Length > 2)
                        {
                            // Rejoin everything after "UNIQUE" and the entity name
                            return string.Join("_", parts.Skip(2));
                        }
                    }

                    return constraintName;
                }

                case PostgresErrorCodes.NotNullViolation:
                {
                    appError = AppErrors.REQUIRED;
                    errorMessage = "Required";
                    return ex.ColumnName;
                }

                case PostgresErrorCodes.ForeignKeyViolation:
                {
                    appError = AppErrors.FOREIGN_KEY_CONSTRAINT_VIOLATION;
                    errorMessage = "Foreign key constraint violation";

                    // ConstraintName = "FOREIGN_KEY_{EntityName}_{Property}_{FKEntity}"
                    var constraintName = ex.ConstraintName;
                    if (!string.IsNullOrWhiteSpace(constraintName) &&
                        constraintName.StartsWith("FOREIGN_KEY_", StringComparison.OrdinalIgnoreCase))
                    {
                        // Parts: ["FOREIGN", "KEY", EntityName, Property, FKEntity]
                        var parts = constraintName.Split('_');
                        if (parts.Length > 3)
                        {
                            return parts[3];
                        }
                    }

                    return constraintName;
                }

                default:
                    return null;
            }
        }
    }
}
