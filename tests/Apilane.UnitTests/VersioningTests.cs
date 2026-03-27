using Apilane.Common.Utilities;
using System.Reflection;

namespace Apilane.UnitTests
{
    [TestClass]
    public class VersioningTests
    {
        // ─── GetVersion ──────────────────────────────────────────────────────────

        [TestMethod]
        public void GetVersion_ReturnsNonEmptyString()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetVersion();
            Assert.IsFalse(string.IsNullOrWhiteSpace(version));
        }

        [TestMethod]
        public void GetVersion_HasThreePartFormat()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetVersion();
            // Format: "Major.Minor.Build"
            var parts = version.Split('.');
            Assert.AreEqual(3, parts.Length);
        }

        [TestMethod]
        public void GetVersion_AllPartsAreNumeric()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetVersion();
            var parts = version.Split('.');
            foreach (var part in parts)
            {
                Assert.IsTrue(int.TryParse(part, out _), $"Part '{part}' is not numeric");
            }
        }

        [TestMethod]
        public void GetVersion_IsDeterministic()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var v1 = assembly.GetVersion();
            var v2 = assembly.GetVersion();
            Assert.AreEqual(v1, v2);
        }
    }
}
