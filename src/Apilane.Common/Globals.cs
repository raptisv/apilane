namespace Apilane.Common
{
    public static class Globals
    {
        public const string AdminRoleName = "Admin";

        public const string PrimaryKeyColumn = "ID";

        public const string OwnerColumn = "Owner";

        public const string CreatedColumn = "Created";

        public const string EntityHistoryDataColumn = "Data";

        public const string EncryptionKey = "dbws_!_@";

        public const string DateTimeMsFormat = "yyyy-MM-dd HH:mm:ss.fff";

        public const string ApplicationTokenQueryParam = "appToken";

        public const string ApplicationTokenHeaderName = "x-application-token";

        public const string ClientIdHeaderName = "x-client-id";

        public const string ClientIdHeaderValuePortal = "portal";

        // Shared secret between the portal and the API. Sent as a header so it never appears in
        // URLs (request logs, proxies, browser history).
        public const string InstallationKeyHeaderName = "x-installation-key";

        // Query-string fallbacks for the two above: a plain browser navigation (e.g. a file
        // download link built as an <a href>) can't set custom request headers, so anything meant
        // to be reachable that way needs these as an alternative to the Authorization/x-client-id
        // headers — same reasoning as ApplicationTokenQueryParam already being a fallback for
        // ApplicationTokenHeaderName.
        public const string AuthTokenQueryParam = "authToken";

        public const string ClientIdQueryParam = "clientId";

        // Signed-request (HMAC proof-of-possession) authentication headers.
        // The secret (AuthTokens.Token) is never transmitted; the client sends only these.
        public const string AuthKeyIdHeaderName = "x-auth-keyid";          // public identifier = AuthTokens.ID

        public const string AuthTimestampHeaderName = "x-auth-timestamp";  // unix milliseconds, signed

        public const string AuthSignatureHeaderName = "x-auth-signature";  // base64 HMAC-SHA256

        // Max allowed clock skew between client timestamp and server time for a signed request.
        public const int SignedRequestClockSkewSeconds = 120;

        public const string ANONYMOUS = "ANONYMOUS";

        public const string AUTHENTICATED = "AUTHENTICATED";

        public const string SCHEMA = "Schema";

        public const string GeneralError = "Something went wrong";

        // The most records a page of a list endpoint (Data/Get, Files/Get, record history, Stats/Aggregate) returns
        public const int MaxPageSize = 1000;

        // The longest an email address can be (RFC 5321). The endpoints that send a mail on request refuse
        // anything longer before they keep it as the key of a rate limit.
        public const int MaxEmailLength = 254;
    }
}
