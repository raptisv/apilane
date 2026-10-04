namespace Apilane.Portal.Api
{
    /// <summary>
    /// Stable, machine-readable error codes returned by the management API. Clients (the Portal UI
    /// and automation) branch on these, never on the message text.
    /// </summary>
    public static class PortalErrorCode
    {
        public const string Validation = "VALIDATION";
        public const string Unauthorized = "UNAUTHORIZED";
        public const string Forbidden = "FORBIDDEN";
        public const string NotFound = "NOT_FOUND";
        public const string Conflict = "CONFLICT";
        public const string TooManyRequests = "TOO_MANY_REQUESTS";
        public const string UpstreamError = "UPSTREAM_ERROR";
        public const string Error = "ERROR";
    }
}
