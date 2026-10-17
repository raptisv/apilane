using Apilane.Net.Extensions;
using System;

namespace Apilane.Net.Models.Data
{
    /// <summary>
    /// Thrown instead of returning the error when a request is built with <c>OnErrorThrowException(true)</c>.
    /// It derives from <see cref="Exception"/> and keeps the message of earlier versions, so existing catches keep working.
    /// </summary>
    public sealed class ApilaneErrorException : Exception
    {
        /// <summary>
        /// The error the API answered: <c>Code</c>, <c>Message</c>, <c>Property</c> and <c>Entity</c>.
        /// </summary>
        public ApilaneError Error { get; }

        /// <summary>
        /// The HTTP status of the answer (the API answers 401 for UNAUTHORIZED and 400 for every other code).
        /// </summary>
        public int StatusCode { get; }

        public ApilaneErrorException(ApilaneError error, int statusCode) : base(error.BuildErrorMessage())
        {
            Error = error;
            StatusCode = statusCode;
        }
    }
}
