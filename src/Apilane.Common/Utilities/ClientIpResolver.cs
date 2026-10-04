namespace Apilane.Common.Utilities
{
    public static class ClientIpResolver
    {
        /// <summary>
        /// Picks the address of the caller from what a request carries: the first entry of the
        /// X-Forwarded-For header when a proxy sent one, otherwise the address of the connection,
        /// otherwise the REMOTE_ADDR header. Returns null when none of them has a value.
        /// </summary>
        public static string? Resolve(string? forwardedForHeader, string? connectionAddress, string? remoteAddrHeader)
        {
            if (!string.IsNullOrWhiteSpace(forwardedForHeader))
            {
                // "client, proxy1, proxy2": the first entry is the original caller.
                var first = forwardedForHeader.TrimEnd(',').Split(',')[0].Trim();
                if (first.Length > 0)
                {
                    return first;
                }
            }

            if (!string.IsNullOrWhiteSpace(connectionAddress))
            {
                return connectionAddress;
            }

            return string.IsNullOrWhiteSpace(remoteAddrHeader) ? null : remoteAddrHeader;
        }
    }
}
