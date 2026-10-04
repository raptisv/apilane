namespace Apilane.Portal.Api
{
    /// <summary>
    /// How often one IP address may call the anonymous account endpoints (sign in, register,
    /// request a password reset). The three share one budget. Configuration section 'AccountRateLimit'.
    /// <para>
    /// The address is the connection's remote address. Behind a reverse proxy that is not on
    /// loopback (another container, an ingress) that is the proxy's address, so every visitor
    /// shares one budget. When the Portal can be reached only through that proxy, set the
    /// environment variable ASPNETCORE_FORWARDEDHEADERS_ENABLED=true so X-Forwarded-For is used
    /// (never when the Portal can also be reached directly: the header is then trusted from any
    /// sender); or raise AccountRateLimit__PermitLimit.
    /// </para>
    /// </summary>
    public class PortalRateLimitOptions
    {
        public const string ConfigurationSection = "AccountRateLimit";

        /// <summary>
        /// Name of the rate limiter policy the three endpoints carry.
        /// </summary>
        public const string AccountPolicy = "PortalAccount";

        /// <summary>
        /// Calls allowed per window and IP address. Must be greater than 0.
        /// </summary>
        public int PermitLimit { get; set; } = 30;

        public int WindowSeconds { get; set; } = 60;
    }
}
