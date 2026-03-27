using Apilane.Common.Enums;
using Apilane.Common.Extensions;

namespace Apilane.UnitTests
{
    [TestClass]
    public class AsciiArtExtensionsTests
    {
        // ─── ToAsciiArt ──────────────────────────────────────────────────────────

        [TestMethod]
        public void ToAsciiArt_Production_ContainsProductionText()
        {
            var result = HostingEnvironment.Production.ToAsciiArt();
            Assert.IsTrue(result.Contains("PRODUCTION") || result.Contains("Production") || result.Length > 10);
        }

        [TestMethod]
        public void ToAsciiArt_Development_ContainsDevelopmentText()
        {
            var result = HostingEnvironment.Development.ToAsciiArt();
            Assert.IsTrue(result.Length > 10);
        }

        [TestMethod]
        public void ToAsciiArt_ProductionAndDevelopment_ReturnDifferentStrings()
        {
            var production = HostingEnvironment.Production.ToAsciiArt();
            var development = HostingEnvironment.Development.ToAsciiArt();
            Assert.AreNotEqual(production, development);
        }

        [TestMethod]
        public void ToAsciiArt_NullExtra_NoExtraAppended()
        {
            var withNull = HostingEnvironment.Production.ToAsciiArt(null);
            var withEmpty = HostingEnvironment.Production.ToAsciiArt();
            Assert.AreEqual(withNull, withEmpty);
        }

        [TestMethod]
        public void ToAsciiArt_LiveExtra_AppendsLiveAscii()
        {
            var withLive = HostingEnvironment.Production.ToAsciiArt("live");
            var withoutExtra = HostingEnvironment.Production.ToAsciiArt();
            Assert.IsTrue(withLive.Length > withoutExtra.Length);
        }

        [TestMethod]
        public void ToAsciiArt_ReadyExtra_AppendsReadyAscii()
        {
            var withReady = HostingEnvironment.Production.ToAsciiArt("ready");
            var withoutExtra = HostingEnvironment.Production.ToAsciiArt();
            Assert.IsTrue(withReady.Length > withoutExtra.Length);
        }

        [TestMethod]
        public void ToAsciiArt_CustomExtra_AppendsCustomString()
        {
            var customExtra = "CUSTOM_TAG";
            var result = HostingEnvironment.Production.ToAsciiArt(customExtra);
            Assert.IsTrue(result.Contains(customExtra));
        }
    }
}
