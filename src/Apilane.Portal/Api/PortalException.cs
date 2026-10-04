using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// A failure the management API reports to its caller: an HTTP status plus a
    /// <see cref="PortalErrorCode"/>. Anything else that escapes an action is treated as a bug
    /// and answered with a generic 500.
    /// </summary>
    public class PortalException : Exception
    {
        public int StatusCode { get; }
        public string Code { get; }
        public string? Entity { get; }
        public string? Property { get; }
        public List<ErrorDetail>? Errors { get; }

        public PortalException(
            int statusCode,
            string code,
            string message,
            string? entity = null,
            string? property = null,
            List<ErrorDetail>? errors = null)
            : base(message)
        {
            StatusCode = statusCode;
            Code = code;
            Entity = entity;
            Property = property;
            Errors = errors;
        }

        public static PortalException Unauthorized()
        {
            return new PortalException(StatusCodes.Status401Unauthorized, PortalErrorCode.Unauthorized, PortalApiErrors.UnauthorizedMessage);
        }

        public static PortalException Forbidden(string message = PortalApiErrors.ForbiddenMessage)
        {
            return new PortalException(StatusCodes.Status403Forbidden, PortalErrorCode.Forbidden, message);
        }

        public static PortalException NotFound(string entity)
        {
            return new PortalException(StatusCodes.Status404NotFound, PortalErrorCode.NotFound, $"{entity} not found.", entity: entity);
        }

        /// <summary>
        /// The request is well formed, but the current state of the resource does not allow it.
        /// </summary>
        public static PortalException Conflict(string message, string? entity = null)
        {
            return new PortalException(StatusCodes.Status409Conflict, PortalErrorCode.Conflict, message, entity: entity);
        }

        /// <summary>
        /// The API server that hosts the application could not be reached, or it failed.
        /// </summary>
        public static PortalException Upstream(string message)
        {
            return new PortalException(StatusCodes.Status502BadGateway, PortalErrorCode.UpstreamError, message);
        }

        /// <summary>
        /// The API server refused the request (it answered 400). The message is the API server's own
        /// text; the property, when it names one, is listed in Errors too.
        /// </summary>
        public static PortalException UpstreamValidation(string message, string? property)
        {
            return new PortalException(
                StatusCodes.Status400BadRequest,
                PortalErrorCode.Validation,
                message,
                property: property,
                errors: string.IsNullOrWhiteSpace(property)
                    ? null
                    : new List<ErrorDetail> { new ErrorDetail { Property = property, Message = message } });
        }

        /// <summary>
        /// Several properties of the request are not acceptable. Same body as a failed model validation.
        /// </summary>
        public static PortalException Validation(List<ErrorDetail> errors)
        {
            return new PortalException(
                StatusCodes.Status400BadRequest,
                PortalErrorCode.Validation,
                PortalApiErrors.ValidationMessage,
                errors: errors);
        }

        /// <summary>
        /// One property of the request is not acceptable. Same body as a failed model validation.
        /// </summary>
        public static PortalException Validation(string property, string message)
        {
            return new PortalException(
                StatusCodes.Status400BadRequest,
                PortalErrorCode.Validation,
                PortalApiErrors.ValidationMessage,
                errors: new List<ErrorDetail> { new ErrorDetail { Property = property, Message = message } });
        }
    }
}
