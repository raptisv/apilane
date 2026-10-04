using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// Builds the one error body the management API uses, for MVC results and for places that
    /// run outside MVC (cookie authentication events, the unknown-route fallback).
    /// </summary>
    public static class PortalApiErrors
    {
        /// <summary>
        /// Requests under this prefix are API calls: they get status codes and JSON, never redirects or HTML.
        /// </summary>
        public const string PathPrefix = "/api";

        public const string UnauthorizedMessage = "Sign in to continue.";
        public const string ForbiddenMessage = "You are not allowed to do this.";
        public const string ValidationMessage = "The request is not valid.";
        public const string ErrorMessage = "Something went wrong.";

        // PascalCase, like the rest of the Apilane JSON surface.
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null
        };

        public static bool IsApiRequest(HttpContext context)
        {
            return context.Request.Path.StartsWithSegments(PathPrefix);
        }

        public static ErrorResponse Create(
            HttpContext context,
            string code,
            string message,
            string? entity = null,
            string? property = null,
            List<ErrorDetail>? errors = null)
        {
            return new ErrorResponse
            {
                Code = code,
                Message = message,
                Entity = entity,
                Property = property,
                Errors = errors,
                TraceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier
            };
        }

        public static IActionResult Result(HttpContext context, PortalException exception)
        {
            return new ObjectResult(Create(context, exception.Code, exception.Message, exception.Entity, exception.Property, exception.Errors))
            {
                StatusCode = exception.StatusCode
            };
        }

        public static IActionResult ValidationResult(HttpContext context, ModelStateDictionary modelState, ActionDescriptor action)
        {
            var errors = new List<ErrorDetail>();

            // When the body can not be read, MVC also reports the action's body parameter as missing
            // ("The request field is required."). The real error is already listed, so that one is left out.
            var bodyParameter = action.Parameters.FirstOrDefault(x => x.BindingInfo?.BindingSource == BindingSource.Body)?.Name;
            var bodyUnreadable = modelState.Keys.Any(x => x.Length == 0 || x.StartsWith('$'));

            foreach (var item in modelState)
            {
                if (bodyUnreadable && item.Key == bodyParameter)
                {
                    continue;
                }

                // A JSON value that could not be read is reported by its JSON path ("$.MailServerPort",
                // or "$" for the whole body) with the parser's own text, which names .NET types.
                // Without the "$." it is the property name every other error uses, with a fixed message.
                var isJsonPath = item.Key.StartsWith('$');
                var property = item.Key.StartsWith("$.", StringComparison.Ordinal) ? item.Key.Substring(2) : item.Key;

                // One message per property. MVC runs [Required] first, so an empty value reports
                // 'Required' and not also the length or format rule.
                foreach (var error in (item.Value?.Errors ?? Enumerable.Empty<ModelError>()).Take(1))
                {
                    errors.Add(new ErrorDetail
                    {
                        Property = property,
                        Message = isJsonPath || string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Invalid value." : error.ErrorMessage
                    });
                }
            }

            return new ObjectResult(Create(context, PortalErrorCode.Validation, ValidationMessage, errors: errors))
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        public static Task WriteAsync(HttpContext context, int statusCode, string code, string message)
        {
            context.Response.StatusCode = statusCode;
            return context.Response.WriteAsJsonAsync(Create(context, code, message), _jsonOptions);
        }
    }
}
