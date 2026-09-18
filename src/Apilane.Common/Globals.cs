namespace Apilane.Common
{
    public static class Globals
    {
        public const string AdminRoleName = "Admin";

        public const string PrimaryKeyColumn = "ID";

        public const string OwnerColumn = "Owner";

        public const string CreatedColumn = "Created";

        public const string EntityHistoryDataColumn = "Data";

        // System entity/column names that need special handling in shared code.
        public const string UsersEntityName = "Users";

        public const string PasswordColumn = "Password";

        public const string EncryptionKey = "dbws_!_@";

        public const string DateTimeMsFormat = "yyyy-MM-dd HH:mm:ss.fff";

        public const string ApplicationTokenQueryParam = "appToken";

        public const string ApplicationTokenHeaderName = "x-application-token";

        public const string ClientIdHeaderName = "x-client-id";

        public const string ClientIdHeaderValuePortal = "portal";

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
    }
}
