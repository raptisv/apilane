using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// The body of every non-2xx answer of the management API.
    /// </summary>
    public class ErrorResponse
    {
        /// <summary>
        /// Stable error code, e.g. UNAUTHORIZED, FORBIDDEN, NOT_FOUND, VALIDATION, CONFLICT, TOO_MANY_REQUESTS, ERROR.
        /// </summary>
        [Required]
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable description. Do not parse it.
        /// </summary>
        [Required]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// The resource the error is about, when there is one.
        /// </summary>
        public string? Entity { get; set; }

        /// <summary>
        /// The request property the error is about, when there is one.
        /// </summary>
        public string? Property { get; set; }

        /// <summary>
        /// Per-property problems of a VALIDATION error.
        /// </summary>
        public List<ErrorDetail>? Errors { get; set; }

        /// <summary>
        /// Identifies the request in the Portal logs.
        /// </summary>
        [Required]
        public string TraceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// One problem with one property of the request.
    /// </summary>
    public class ErrorDetail
    {
        /// <summary>
        /// The request property, e.g. ServerUrl. "$" or empty when the body as a whole could not be read.
        /// </summary>
        [Required]
        public string Property { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;
    }
}
