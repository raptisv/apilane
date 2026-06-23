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
    }
}
