using Apilane.Common.Utilities;

namespace Apilane.UnitTests
{
    /// <summary>
    /// The TrustedProxies setting: which part of X-Forwarded-For is believed. Every case is an address a caller can
    /// choose (a forged header) or a real caller behind a proxy, so each says who is who.
    /// </summary>
    [TestClass]
    public class ForwardedHeaderTrustTests
    {
        private const string Proxy = "172.18.0.2";
        private static readonly ForwardedHeaderTrust ThatProxy = ForwardedHeaderTrust.FromSettings(new[] { Proxy });

        // ─── The setting ─────────────────────────────────────────────────────────

        [TestMethod]
        public void FromSettings_NothingSet_IsNotConfigured()
        {
            Assert.AreEqual(ForwardedHeaderMode.NotConfigured, ForwardedHeaderTrust.FromSettings(null).Mode);
            Assert.AreEqual(ForwardedHeaderMode.NotConfigured, ForwardedHeaderTrust.FromSettings(Array.Empty<string>()).Mode);
            Assert.AreEqual(ForwardedHeaderMode.NotConfigured, ForwardedHeaderTrust.FromSettings(new string?[] { null, "", "  " }).Mode);
        }

        [TestMethod]
        public void FromSettings_None_IsNoProxies()
        {
            Assert.AreEqual(ForwardedHeaderMode.NoProxies, ForwardedHeaderTrust.FromSettings(new[] { "none" }).Mode);
            Assert.AreEqual(ForwardedHeaderMode.NoProxies, ForwardedHeaderTrust.FromSettings(new[] { " NONE " }).Mode);
        }

        [TestMethod]
        public void FromSettings_ListsOrACommaSeparatedValue_AreProxiesInTheirCanonicalText()
        {
            var trust = ForwardedHeaderTrust.FromSettings(new[] { "10.0.0.0/8, 172.18.0.2", "2001:DB8:0::/32", "10.0.0.0/8" });

            Assert.AreEqual(ForwardedHeaderMode.TrustedProxies, trust.Mode);
            CollectionAssert.AreEqual(new[] { "10.0.0.0/8", "172.18.0.2", "2001:db8::/32" }, trust.Proxies.ToArray());
        }

        [TestMethod]
        [DataRow("not an address")]
        [DataRow("10.0.0.0/33")]
        [DataRow("10.1.2.3/8")]
        public void FromSettings_AValueThatIsNotValid_StopsTheStartWithItsName(string value)
        {
            var error = Assert.ThrowsExactly<FormatException>(() => ForwardedHeaderTrust.FromSettings(new[] { "10.0.0.1", value }));

            StringAssert.Contains(error.Message, value);
            StringAssert.Contains(error.Message, "TrustedProxies");
        }

        [TestMethod]
        public void FromSettings_NoneBesideAnAddress_IsRefused()
        {
            Assert.ThrowsExactly<FormatException>(() => ForwardedHeaderTrust.FromSettings(new[] { "none", "10.0.0.1" }));
        }

        // ─── Not configured: what every version before the setting did ───────────

        [TestMethod]
        public void NotConfigured_BelievesTheFirstEntryOfTheHeader_FromAnyone()
        {
            var result = ClientIpResolver.Resolve(ForwardedHeaderTrust.NotConfigured, "10.9.9.9, 203.0.113.7", "198.51.100.1", null);

            Assert.AreEqual("10.9.9.9", result.Address);
            Assert.AreEqual(ClientAddressSource.ForwardedFor, result.Source);
            Assert.IsTrue(result.ForwardedForHonoured);
        }

        [TestMethod]
        public void NotConfigured_WithoutTheHeader_IsTheConnection()
        {
            var result = ClientIpResolver.Resolve(ForwardedHeaderTrust.NotConfigured, null, "198.51.100.1", "10.0.0.1");

            Assert.AreEqual("198.51.100.1", result.Address);
            Assert.AreEqual(ClientAddressSource.Connection, result.Source);
        }

        [TestMethod]
        public void NotConfigured_WithoutAConnection_FallsBackToTheRemoteAddrHeader()
        {
            var result = ClientIpResolver.Resolve(ForwardedHeaderTrust.NotConfigured, null, null, "10.0.0.1");

            Assert.AreEqual("10.0.0.1", result.Address);
            Assert.AreEqual(ClientAddressSource.RemoteAddrHeader, result.Source);
        }

        [TestMethod]
        public void NotConfigured_OldResolve_StillAnswersTheSameWay()
        {
            Assert.AreEqual("1.1.1.1", ClientIpResolver.Resolve("1.1.1.1, 2.2.2.2", "3.3.3.3", null));
            Assert.AreEqual("3.3.3.3", ClientIpResolver.Resolve(null, "3.3.3.3", "4.4.4.4"));
            Assert.AreEqual("4.4.4.4", ClientIpResolver.Resolve(null, null, "4.4.4.4"));
            Assert.IsNull(ClientIpResolver.Resolve(null, null, null));
        }

        // ─── TrustedProxies ──────────────────────────────────────────────────────

        [TestMethod]
        public void Trusted_ACallerBehindTheProxy_IsTheAddressTheProxySaw()
        {
            var result = ClientIpResolver.Resolve(ThatProxy, "203.0.113.7", Proxy, null);

            Assert.AreEqual("203.0.113.7", result.Address);
            Assert.AreEqual(ClientAddressSource.ForwardedFor, result.Source);
            Assert.IsTrue(result.ForwardedForHonoured);
        }

        [TestMethod]
        public void Trusted_AForgedEntryBeforeTheRealOne_IsNotBelieved()
        {
            // The caller sent "X-Forwarded-For: 10.0.0.5"; a proxy that appends added the address it saw
            var result = ClientIpResolver.Resolve(ThatProxy, "10.0.0.5, 203.0.113.7", Proxy, null);

            Assert.AreEqual("203.0.113.7", result.Address);
        }

        [TestMethod]
        public void Trusted_ChainOfProxies_StopsAtTheFirstEntryThatIsNotAProxy()
        {
            var trust = ForwardedHeaderTrust.FromSettings(new[] { "172.18.0.0/16", "10.0.0.0/8" });

            // client, then two proxies of the list
            var result = ClientIpResolver.Resolve(trust, "6.6.6.6, 203.0.113.7, 10.1.1.1, 172.18.0.9", "172.18.0.2", null);

            Assert.AreEqual("203.0.113.7", result.Address);
        }

        [TestMethod]
        public void Trusted_AConnectionThatIsNotAProxy_IgnoresTheHeader()
        {
            // Someone who reaches the server without the proxy writes any header they like
            var result = ClientIpResolver.Resolve(ThatProxy, "10.0.0.5", "198.51.100.1", null);

            Assert.AreEqual("198.51.100.1", result.Address);
            Assert.AreEqual(ClientAddressSource.Connection, result.Source);
            Assert.IsFalse(result.ForwardedForHonoured);
        }

        [TestMethod]
        public void Trusted_AProxyWithoutTheHeader_IsTheProxyItself()
        {
            var result = ClientIpResolver.Resolve(ThatProxy, null, Proxy, null);

            Assert.AreEqual(Proxy, result.Address);
            Assert.AreEqual(ClientAddressSource.Connection, result.Source);
        }

        [TestMethod]
        public void Trusted_EveryEntryAProxy_IsTheConnection()
        {
            var result = ClientIpResolver.Resolve(ThatProxy, "172.18.0.2, 172.18.0.2", Proxy, null);

            Assert.AreEqual(Proxy, result.Address);
        }

        [TestMethod]
        [DataRow("203.0.113.7:51234", "203.0.113.7")]
        [DataRow("[2001:db8::7]:51234", "2001:db8::7")]
        [DataRow("[2001:db8::7]", "2001:db8::7")]
        [DataRow("2001:db8::7", "2001:db8::7")]
        public void Trusted_APortAfterTheAddress_IsDropped(string entry, string expected)
        {
            Assert.AreEqual(expected, ClientIpResolver.Resolve(ThatProxy, entry, Proxy, null).Address);
        }

        [TestMethod]
        public void Trusted_TheRemoteAddrHeader_IsNeverRead()
        {
            var result = ClientIpResolver.Resolve(ThatProxy, null, null, "10.0.0.5");

            Assert.AreEqual(string.Empty, result.Address);
            Assert.AreEqual(ClientAddressSource.None, result.Source);
        }

        // ─── Several proxies in a row ────────────────────────────────────────────

        [TestMethod]
        public void Chain_AllTheProxiesListed_TheCallerIsTheFirstAddressThatIsNotOne()
        {
            // client -> a CDN edge (172.70.1.2) -> the proxy of the origin (172.30.0.1) -> the API server.
            // The proxy of the origin appended the edge address to what the CDN sent: "<what the caller wrote>, <caller>, <edge>".
            var trust = ForwardedHeaderTrust.FromSettings(new[] { "172.30.0.1", "172.64.0.0/13" });

            var result = ClientIpResolver.Resolve(trust, "6.6.6.6, 203.0.113.7, 172.70.1.2", "172.30.0.1", null);

            Assert.AreEqual("203.0.113.7", result.Address);
        }

        [TestMethod]
        public void Chain_AProxyThatIsNotListed_IsTakenForTheCaller()
        {
            // The mistake the Portal explains: only the proxy of the origin is listed
            var trust = ForwardedHeaderTrust.FromSettings(new[] { "172.30.0.1" });

            var result = ClientIpResolver.Resolve(trust, "203.0.113.7, 172.70.1.2", "172.30.0.1", null);

            Assert.AreEqual("172.70.1.2", result.Address);
        }

        // ─── none ────────────────────────────────────────────────────────────────

        [TestMethod]
        public void None_TheHeaderIsNeverBelieved()
        {
            var trust = ForwardedHeaderTrust.FromSettings(new[] { "none" });

            var result = ClientIpResolver.Resolve(trust, "10.0.0.5", "198.51.100.1", "10.0.0.6");

            Assert.AreEqual("198.51.100.1", result.Address);
            Assert.AreEqual(ClientAddressSource.Connection, result.Source);
            Assert.IsFalse(result.ForwardedForHonoured);
        }
    }
}
