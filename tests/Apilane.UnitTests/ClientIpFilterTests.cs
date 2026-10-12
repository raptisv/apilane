using Apilane.Common.Utilities;

namespace Apilane.UnitTests
{
    /// <summary>
    /// The list of client addresses of an application: single addresses, networks (CIDR) and ranges, IPv4 and IPv6.
    /// </summary>
    [TestClass]
    public class ClientIpFilterTests
    {
        // ─── IsMatch ─────────────────────────────────────────────────────────────

        [TestMethod]
        [DataRow("10.0.0.1", "10.0.0.1", true)]
        [DataRow("10.0.0.1", "10.0.0.2", false)]
        [DataRow("10.0.0.1,10.0.0.2", "10.0.0.2", true)]
        [DataRow(" 10.0.0.1 , ,10.0.0.2", "10.0.0.2", true)]
        public void IsMatch_SingleAddresses(string list, string client, bool expected)
        {
            Assert.AreEqual(expected, ClientIpFilter.IsMatch(list, client));
        }

        [TestMethod]
        [DataRow("10.0.0.0/8", "10.0.0.0", true)]
        [DataRow("10.0.0.0/8", "10.255.255.255", true)]
        [DataRow("10.0.0.0/8", "11.0.0.0", false)]
        [DataRow("10.0.0.0/8", "9.255.255.255", false)]
        [DataRow("192.168.1.0/24", "192.168.1.77", true)]
        [DataRow("192.168.1.0/24", "192.168.2.1", false)]
        [DataRow("203.0.113.5/32", "203.0.113.5", true)]
        [DataRow("203.0.113.5/32", "203.0.113.6", false)]
        [DataRow("0.0.0.0/0", "8.8.8.8", true)]
        [DataRow("172.16.0.0/12", "172.31.255.255", true)]
        [DataRow("172.16.0.0/12", "172.32.0.0", false)]
        [DataRow("10.0.0.0/8,192.168.0.0/16", "192.168.5.5", true)]
        public void IsMatch_Networks(string list, string client, bool expected)
        {
            Assert.AreEqual(expected, ClientIpFilter.IsMatch(list, client));
        }

        [TestMethod]
        [DataRow("10.0.0.10-10.0.0.20", "10.0.0.10", true)]
        [DataRow("10.0.0.10-10.0.0.20", "10.0.0.20", true)]
        [DataRow("10.0.0.10-10.0.0.20", "10.0.0.15", true)]
        [DataRow("10.0.0.10-10.0.0.20", "10.0.0.9", false)]
        [DataRow("10.0.0.10-10.0.0.20", "10.0.0.21", false)]
        [DataRow("10.0.0.250-10.0.1.5", "10.0.1.0", true)]
        [DataRow("10.0.0.250-10.0.1.5", "10.0.1.6", false)]
        public void IsMatch_Ranges(string list, string client, bool expected)
        {
            Assert.AreEqual(expected, ClientIpFilter.IsMatch(list, client));
        }

        [TestMethod]
        [DataRow("2001:db8::/32", "2001:db8:ffff::1", true)]
        [DataRow("2001:db8::/32", "2001:db9::1", false)]
        [DataRow("2001:db8::1", "2001:DB8:0:0:0:0:0:1", true)]
        [DataRow("2001:db8::1-2001:db8::9", "2001:db8::5", true)]
        [DataRow("2001:db8::1-2001:db8::9", "2001:db8::a", false)]
        public void IsMatch_IPv6(string list, string client, bool expected)
        {
            Assert.AreEqual(expected, ClientIpFilter.IsMatch(list, client));
        }

        [TestMethod]
        public void IsMatch_AnIPv4AddressInAnIPv6Socket_IsTheIPv4Address()
        {
            // A dual-stack server reports an IPv4 caller as ::ffff:a.b.c.d
            Assert.IsTrue(ClientIpFilter.IsMatch("10.0.0.0/8", "::ffff:10.1.2.3"));
            Assert.IsTrue(ClientIpFilter.IsMatch("10.1.2.3", "::ffff:10.1.2.3"));
            Assert.IsFalse(ClientIpFilter.IsMatch("10.0.0.0/8", "::ffff:11.1.2.3"));
        }

        [TestMethod]
        public void IsMatch_TheFamiliesDoNotMix()
        {
            Assert.IsFalse(ClientIpFilter.IsMatch("10.0.0.0/8", "2001:db8::1"));
            Assert.IsFalse(ClientIpFilter.IsMatch("2001:db8::/32", "10.0.0.1"));
            Assert.IsFalse(ClientIpFilter.IsMatch("::/0", "10.0.0.1"));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("not an address")]
        [DataRow("10.0.0.1:8080")]
        public void IsMatch_ACallerWithoutAUsableAddress_IsInNoList(string client)
        {
            Assert.IsFalse(ClientIpFilter.IsMatch("0.0.0.0/0,::/0", client));
        }

        [TestMethod]
        public void IsMatch_AnEntryThatIsNotValid_NeverMatchesBesidesItsOwnText()
        {
            // 255.255.255.255 is what a lazily parsed bad entry could turn into: it must not match
            Assert.IsFalse(ClientIpFilter.IsMatch("garbage,10.0.0.0/33,10.0.0.9-10.0.0.1", "255.255.255.255"));
            Assert.IsFalse(ClientIpFilter.IsMatch("garbage,10.0.0.0/33,10.0.0.9-10.0.0.1", "10.0.0.5"));

            // A list written before ranges compared text: an entry that is the caller's address as written still matches
            Assert.IsTrue(ClientIpFilter.IsMatch("garbage", "garbage"));
        }

        [TestMethod]
        public void IsMatch_EmptyList_MatchesNothing()
        {
            Assert.IsFalse(ClientIpFilter.IsMatch(null, "10.0.0.1"));
            Assert.IsFalse(ClientIpFilter.IsMatch("", "10.0.0.1"));
        }

        // ─── TryNormalize ────────────────────────────────────────────────────────

        [TestMethod]
        [DataRow("10.0.0.1", "10.0.0.1")]
        [DataRow("  10.0.0.1  ", "10.0.0.1")]
        [DataRow("10.0.0.0/8", "10.0.0.0/8")]
        [DataRow("10.0.0.0 / 8", "10.0.0.0/8")]
        [DataRow("10.0.0.1-10.0.0.50", "10.0.0.1-10.0.0.50")]
        [DataRow("10.0.0.1 - 10.0.0.50", "10.0.0.1-10.0.0.50")]
        [DataRow("10.0.0.5-10.0.0.5", "10.0.0.5-10.0.0.5")]
        [DataRow("2001:DB8:0:0:0:0:0:1", "2001:db8::1")]
        [DataRow("2001:db8:0:0::/32", "2001:db8::/32")]
        [DataRow("::ffff:10.0.0.1", "10.0.0.1")]
        public void TryNormalize_AcceptsAndWritesTheCanonicalText(string entry, string expected)
        {
            Assert.IsTrue(ClientIpFilter.TryNormalize(entry, out var normalized, out var error), error);
            Assert.AreEqual(expected, normalized);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("hello")]
        [DataRow("10.0.0")]
        [DataRow("010.0.0.1")]
        [DataRow("+10.0.0.1")]
        [DataRow("10.0.0.256")]
        [DataRow("10.0.0.1 10.0.0.2")]
        [DataRow("16909060")]
        [DataRow("10.0.0.0/33")]
        [DataRow("10.0.0.0/")]
        [DataRow("10.0.0.0/08")]
        [DataRow("10.0.0.0/-1")]
        [DataRow("10.0.0.0/8/8")]
        [DataRow("2001:db8::/129")]
        [DataRow("10.0.0.9-10.0.0.1")]
        [DataRow("10.0.0.1-2001:db8::1")]
        [DataRow("10.0.0.1-10.0.0.2-10.0.0.3")]
        [DataRow("10.0.0.1-")]
        [DataRow("fe80::1%eth0")]
        [DataRow("[::1]")]
        public void TryNormalize_Refuses(string entry)
        {
            Assert.IsFalse(ClientIpFilter.TryNormalize(entry, out var normalized, out var error), $"'{entry}' was accepted as '{normalized}'");
            Assert.AreEqual(string.Empty, normalized);
            Assert.AreNotEqual(string.Empty, error);
        }

        [TestMethod]
        public void TryNormalize_ANetworkWithHostBits_SaysHowToWriteIt()
        {
            Assert.IsFalse(ClientIpFilter.TryNormalize("10.1.2.3/8", out _, out var error));
            StringAssert.Contains(error, "10.0.0.0/8");
        }

        [TestMethod]
        public void TryNormalize_AMoreThanOneHundredCharacterEntry_IsRefused()
        {
            Assert.IsFalse(ClientIpFilter.TryNormalize(new string('1', 101), out _, out _));
        }
    }
}
