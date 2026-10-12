using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Apilane.Common.Utilities
{
    /// <summary>
    /// The list of client addresses of an application (Block or Allow): every entry is one of
    ///
    ///   10.0.0.1            a single address (IPv4 or IPv6)
    ///   10.0.0.0/8          a network, in CIDR notation (IPv4 /0 to /32, IPv6 /0 to /128)
    ///   10.0.0.1-10.0.0.50  a range of addresses of one family, both ends included
    ///
    /// The Portal validates an entry and stores it in its canonical text (<see cref="TryNormalize"/>); the API
    /// server matches the address of a request against the stored list (<see cref="IsMatch"/>).
    /// </summary>
    public static class ClientIpFilter
    {
        /// <summary>
        /// An entry as bytes: the first and the last address it covers. An entry that is not valid has none, and
        /// only matches an address that is written exactly like it (the lists written before ranges were exact
        /// text comparisons).
        /// </summary>
        private sealed record Entry(string Text, byte[]? From, byte[]? To)
        {
            public bool Contains(IPAddress address)
            {
                if (From is null || To is null)
                {
                    return false;
                }

                var bytes = address.GetAddressBytes();

                return bytes.Length == From.Length && Compare(bytes, From) >= 0 && Compare(bytes, To) <= 0;
            }
        }

        // A list is parsed once, not on every request. The values are the lists of applications, so this stays
        // small; it is emptied when it grows beyond what a deployment can hold.
        private static readonly ConcurrentDictionary<string, Entry[]> Parsed = new();
        private const int MaxCachedLists = 2048;

        /// <summary>
        /// Checks one entry of a list as the Portal accepts it, and returns it in the text that is stored.
        /// On a refusal <paramref name="error"/> completes the sentence "'entry' ...".
        /// </summary>
        public static bool TryNormalize(string? text, out string normalized, out string error)
        {
            normalized = string.Empty;
            error = string.Empty;

            var value = text?.Trim() ?? string.Empty;

            if (value.Length == 0 || value.Length > 100)
            {
                error = "is not an address, a range or a network";
                return false;
            }

            if (!TryParseEntry(value, out var from, out var to, out normalized, out error))
            {
                normalized = string.Empty;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether the address of a request is in the list.
        /// </summary>
        public static bool IsMatch(string? list, string? clientAddress)
        {
            if (string.IsNullOrWhiteSpace(list) || string.IsNullOrWhiteSpace(clientAddress))
            {
                return false;
            }

            var client = clientAddress.Trim();
            var entries = Parse(list);

            if (entries.Any(x => x.Text.Equals(client, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return IPAddress.TryParse(client, out var address)
                && entries.Any(x => x.Contains(Unmap(address)));
        }

        private static Entry[] Parse(string list)
        {
            if (Parsed.TryGetValue(list, out var cached))
            {
                return cached;
            }

            var entries = new List<Entry>();

            foreach (var raw in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var text = raw.Trim();

                if (text.Length == 0)
                {
                    continue;
                }

                entries.Add(TryParseEntry(text, out var from, out var to, out _, out _)
                    ? new Entry(text, from, to)
                    : new Entry(text, null, null));
            }

            var result = entries.ToArray();

            if (Parsed.Count >= MaxCachedLists)
            {
                Parsed.Clear();
            }

            Parsed[list] = result;
            return result;
        }

        private static bool TryParseEntry(string value, out byte[] from, out byte[] to, out string normalized, out string error)
        {
            from = Array.Empty<byte>();
            to = Array.Empty<byte>();
            normalized = string.Empty;

            var slash = value.IndexOf('/');
            var dash = value.IndexOf('-');

            if (slash >= 0)
            {
                if (!TryParseNetwork(value, slash, out var network, out var prefix, out from, out to, out error))
                {
                    return false;
                }

                normalized = $"{network}/{prefix}";
                return true;
            }

            if (dash >= 0)
            {
                if (!TryParseRange(value, dash, out var start, out var end, out error))
                {
                    return false;
                }

                from = start.GetAddressBytes();
                to = end.GetAddressBytes();
                normalized = $"{start}-{end}";
                return true;
            }

            if (!TryParseAddress(value, out var address, out error))
            {
                return false;
            }

            from = address.GetAddressBytes();
            to = from;
            normalized = address.ToString();
            return true;
        }

        private static bool TryParseNetwork(
            string value, int slash, out IPAddress network, out int prefix, out byte[] from, out byte[] to, out string error)
        {
            network = IPAddress.None;
            prefix = 0;
            from = Array.Empty<byte>();
            to = Array.Empty<byte>();

            if (value.IndexOf('/', slash + 1) >= 0 || value.Contains('-'))
            {
                error = "is not a network: use an address, a slash and a prefix length";
                return false;
            }

            if (!TryParseAddress(value[..slash].Trim(), out network, out error))
            {
                return false;
            }

            if (network.AddressFamily == AddressFamily.InterNetworkV6 && value[..slash].Contains('.'))
            {
                error = "mixes dotted and hexadecimal notation: write the IPv6 network in hexadecimal";
                return false;
            }

            var bytes = network.GetAddressBytes();
            var maxPrefix = bytes.Length * 8;
            var prefixText = value[(slash + 1)..].Trim();

            if (prefixText.Length is 0 or > 3 || !prefixText.All(char.IsAsciiDigit)
                || (prefixText.Length > 1 && prefixText[0] == '0')
                || !int.TryParse(prefixText, out prefix) || prefix > maxPrefix)
            {
                error = $"has no valid prefix length (0 to {maxPrefix} for {(bytes.Length == 4 ? "IPv4" : "IPv6")})";
                return false;
            }

            from = (byte[])bytes.Clone();
            to = (byte[])bytes.Clone();

            // The host part must be zero, so one network has one way to be written
            for (var bit = prefix; bit < maxPrefix; bit++)
            {
                var mask = (byte)(0x80 >> (bit % 8));

                if ((bytes[bit / 8] & mask) != 0)
                {
                    var host = (byte[])bytes.Clone();
                    for (var i = prefix; i < maxPrefix; i++)
                    {
                        host[i / 8] &= (byte)~(0x80 >> (i % 8));
                    }

                    error = $"has bits set beyond its /{prefix} prefix: write it as {new IPAddress(host)}/{prefix}";
                    return false;
                }

                to[bit / 8] |= mask;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryParseRange(string value, int dash, out IPAddress start, out IPAddress end, out string error)
        {
            start = IPAddress.None;
            end = IPAddress.None;

            if (value.IndexOf('-', dash + 1) >= 0)
            {
                error = "is not a range: use an address, a hyphen and another address";
                return false;
            }

            if (!TryParseAddress(value[..dash].Trim(), out start, out error)
                || !TryParseAddress(value[(dash + 1)..].Trim(), out end, out error))
            {
                return false;
            }

            var from = start.GetAddressBytes();
            var to = end.GetAddressBytes();

            if (from.Length != to.Length)
            {
                error = "mixes an IPv4 and an IPv6 address";
                return false;
            }

            if (Compare(from, to) > 0)
            {
                error = "ends before it begins";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// A plain address: IPv4 as four numbers without leading zeros, or IPv6. An IPv6 address that holds an IPv4
        /// one (::ffff:10.0.0.1) is that IPv4 address.
        /// </summary>
        private static bool TryParseAddress(string text, out IPAddress address, out string error)
        {
            address = IPAddress.None;
            error = "is not a valid IPv4 or IPv6 address";

            if (text.Length == 0 || text.Contains('%') || text.Any(char.IsWhiteSpace) || text.StartsWith('[')
                || !IPAddress.TryParse(text, out var parsed))
            {
                return false;
            }

            if (parsed.AddressFamily == AddressFamily.InterNetwork)
            {
                // Only the plain dotted form: the round trip refuses signs, short forms and leading zeros
                if (parsed.ToString() != text)
                {
                    return false;
                }
            }
            else if (parsed.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return false;
            }

            address = Unmap(parsed);
            return true;
        }

        private static IPAddress Unmap(IPAddress address)
        {
            return address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6
                ? address.MapToIPv4()
                : address;
        }

        private static int Compare(byte[] left, byte[] right)
        {
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return left[i] < right[i] ? -1 : 1;
                }
            }

            return 0;
        }
    }
}
