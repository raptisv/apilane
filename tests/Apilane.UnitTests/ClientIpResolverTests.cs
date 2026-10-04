using Apilane.Common.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Apilane.UnitTests
{
    [TestClass]
    public class ClientIpResolverTests
    {
        [TestMethod]
        [DataRow("203.0.113.7", "10.0.0.1", null, "203.0.113.7")]
        [DataRow("203.0.113.7, 10.0.0.2, 10.0.0.3", "10.0.0.1", null, "203.0.113.7")]
        [DataRow("  203.0.113.7  ,10.0.0.2", "10.0.0.1", null, "203.0.113.7")]
        [DataRow("203.0.113.7,", "10.0.0.1", null, "203.0.113.7")]
        [DataRow("2001:db8::1, 10.0.0.2", "10.0.0.1", null, "2001:db8::1")]
        public void Resolve_ForwardedFor_Should_Return_First_Entry(string forwardedFor, string connection, string? remoteAddr, string expected)
        {
            Assert.AreEqual(expected, ClientIpResolver.Resolve(forwardedFor, connection, remoteAddr));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow(",,,")]
        [DataRow(" , 203.0.113.7")]
        public void Resolve_ForwardedFor_Without_First_Entry_Should_Return_Connection_Address(string? forwardedFor)
        {
            Assert.AreEqual("10.0.0.1", ClientIpResolver.Resolve(forwardedFor, "10.0.0.1", "198.51.100.9"));
        }

        [TestMethod]
        public void Resolve_Without_Connection_Address_Should_Return_RemoteAddr_Header()
        {
            Assert.AreEqual("198.51.100.9", ClientIpResolver.Resolve(null, null, "198.51.100.9"));
        }

        [TestMethod]
        [DataRow(null, null, null)]
        [DataRow("", "", "")]
        [DataRow(" ", null, "  ")]
        public void Resolve_Nothing_Should_Return_Null(string? forwardedFor, string? connection, string? remoteAddr)
        {
            Assert.IsNull(ClientIpResolver.Resolve(forwardedFor, connection, remoteAddr));
        }
    }
}
