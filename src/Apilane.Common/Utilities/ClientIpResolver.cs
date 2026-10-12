using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Apilane.Common.Utilities
{
    /// <summary>
    /// Where the address of a caller came from.
    /// </summary>
    public enum ClientAddressSource
    {
        /// <summary>The address of the connection that reached the server.</summary>
        Connection,
        /// <summary>An entry of the X-Forwarded-For header.</summary>
        ForwardedFor,
        /// <summary>The REMOTE_ADDR header: only when nothing else is known, and only without TrustedProxies.</summary>
        RemoteAddrHeader,
        /// <summary>No address could be found.</summary>
        None
    }

    /// <summary>
    /// How much the server believes of the X-Forwarded-For header, set with the TrustedProxies setting.
    /// </summary>
    public enum ForwardedHeaderMode
    {
        /// <summary>
        /// Nothing is set: the first entry of the header is believed, from anyone. Anyone who can reach the
        /// server can write the address that the IP lists of an application see.
        /// </summary>
        NotConfigured,

        /// <summary>
        /// The header is read only when the connection comes from a listed proxy, from its right: the first entry
        /// that is not a listed proxy is the caller.
        /// </summary>
        TrustedProxies,

        /// <summary>
        /// The setting is "none": the header is never read, the address is that of the connection. For servers that
        /// callers reach directly.
        /// </summary>
        NoProxies
    }

    /// <summary>
    /// The TrustedProxies setting: addresses, networks or ranges (as in the IP list of an application) of the proxies
    /// that sit in front of the server, or the one word "none".
    /// </summary>
    public sealed class ForwardedHeaderTrust
    {
        public const string NoneKeyword = "none";

        public static readonly ForwardedHeaderTrust NotConfigured = new(ForwardedHeaderMode.NotConfigured, Array.Empty<string>());

        public ForwardedHeaderMode Mode { get; }

        /// <summary>The proxies, in their canonical text. Empty unless the mode is TrustedProxies.</summary>
        public IReadOnlyList<string> Proxies { get; }

        private readonly string _list;

        private ForwardedHeaderTrust(ForwardedHeaderMode mode, IReadOnlyList<string> proxies)
        {
            Mode = mode;
            Proxies = proxies;
            _list = string.Join(",", proxies);
        }

        /// <summary>
        /// Reads the values of the setting. Throws a <see cref="FormatException"/> that names the first value that is
        /// not valid: a server must not start with a list it would silently ignore.
        /// </summary>
        public static ForwardedHeaderTrust FromSettings(IEnumerable<string?>? values)
        {
            var entries = (values ?? Enumerable.Empty<string?>())
                .SelectMany(x => (x ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();

            if (entries.Count == 0)
            {
                return NotConfigured;
            }

            if (entries.Any(x => x.Equals(NoneKeyword, StringComparison.OrdinalIgnoreCase)))
            {
                if (entries.Count > 1)
                {
                    throw new FormatException($"TrustedProxies: '{NoneKeyword}' cannot be combined with other values.");
                }

                return new ForwardedHeaderTrust(ForwardedHeaderMode.NoProxies, Array.Empty<string>());
            }

            var proxies = new List<string>();

            foreach (var entry in entries)
            {
                if (!ClientIpFilter.TryNormalize(entry, out var normalized, out var problem))
                {
                    throw new FormatException($"TrustedProxies: '{entry}' {problem}.");
                }

                if (!proxies.Contains(normalized))
                {
                    proxies.Add(normalized);
                }
            }

            return new ForwardedHeaderTrust(ForwardedHeaderMode.TrustedProxies, proxies);
        }

        public bool IsProxy(string? address) => ClientIpFilter.IsMatch(_list, address);
    }

    /// <summary>
    /// The address of a caller, and how it was found.
    /// </summary>
    /// <param name="Address">The address the IP lists and the log see. Empty when none was found.</param>
    /// <param name="Source">Where it came from.</param>
    /// <param name="ForwardedForHonoured">True when the X-Forwarded-For header decided the address.</param>
    public sealed record ClientAddress(string Address, ClientAddressSource Source, bool ForwardedForHonoured);

    public static class ClientIpResolver
    {
        /// <summary>
        /// The address of the caller when nothing is configured: the first entry of the X-Forwarded-For header when
        /// a proxy sent one, otherwise the address of the connection, otherwise the REMOTE_ADDR header. Returns null
        /// when none of them has a value.
        /// </summary>
        public static string? Resolve(string? forwardedForHeader, string? connectionAddress, string? remoteAddrHeader)
        {
            var address = Resolve(ForwardedHeaderTrust.NotConfigured, forwardedForHeader, connectionAddress, remoteAddrHeader).Address;

            return address.Length == 0 ? null : address;
        }

        /// <summary>
        /// Picks the address of the caller from what a request carries, as far as the TrustedProxies setting allows.
        ///
        /// NotConfigured keeps the behaviour of every version before the setting existed. With TrustedProxies the
        /// header is read only when the connection comes from a listed proxy, and from its right: each proxy appends
        /// the address it saw, so the entries to the left of the last listed proxy are whatever the caller wrote.
        /// With "none" the header is never read. In both of these the REMOTE_ADDR header, which any caller can send, is
        /// never read either.
        /// </summary>
        public static ClientAddress Resolve(ForwardedHeaderTrust trust, string? forwardedForHeader, string? connectionAddress, string? remoteAddrHeader)
        {
            var connection = string.IsNullOrWhiteSpace(connectionAddress) ? null : connectionAddress.Trim();

            if (trust.Mode == ForwardedHeaderMode.NotConfigured)
            {
                if (!string.IsNullOrWhiteSpace(forwardedForHeader))
                {
                    // "client, proxy1, proxy2": the first entry is the original caller.
                    var first = forwardedForHeader.TrimEnd(',').Split(',')[0].Trim();
                    if (first.Length > 0)
                    {
                        return new ClientAddress(first, ClientAddressSource.ForwardedFor, true);
                    }
                }

                if (connection is not null)
                {
                    return new ClientAddress(connection, ClientAddressSource.Connection, false);
                }

                return string.IsNullOrWhiteSpace(remoteAddrHeader)
                    ? new ClientAddress(string.Empty, ClientAddressSource.None, false)
                    : new ClientAddress(remoteAddrHeader.Trim(), ClientAddressSource.RemoteAddrHeader, false);
            }

            if (connection is null)
            {
                return new ClientAddress(string.Empty, ClientAddressSource.None, false);
            }

            if (trust.Mode == ForwardedHeaderMode.TrustedProxies
                && trust.IsProxy(connection)
                && !string.IsNullOrWhiteSpace(forwardedForHeader))
            {
                var entries = forwardedForHeader.Split(',')
                    .Select(x => StripPort(x.Trim()))
                    .Where(x => x.Length > 0)
                    .ToList();

                // From the right: skip the proxies, the first other entry is the caller as the nearest proxy saw it.
                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    if (!trust.IsProxy(entries[i]))
                    {
                        return new ClientAddress(entries[i], ClientAddressSource.ForwardedFor, true);
                    }
                }
            }

            return new ClientAddress(connection, ClientAddressSource.Connection, false);
        }

        /// <summary>
        /// 203.0.113.7:51234 and [2001:db8::1]:51234 are an address and a port: the address is what is compared.
        /// </summary>
        private static string StripPort(string entry)
        {
            if (entry.StartsWith('[') && entry.IndexOf(']') is var close and > 0)
            {
                return entry[1..close];
            }

            var colon = entry.IndexOf(':');

            if (colon > 0 && entry.IndexOf(':', colon + 1) < 0
                && IPAddress.TryParse(entry[..colon], out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return entry[..colon];
            }

            return entry;
        }
    }
}
