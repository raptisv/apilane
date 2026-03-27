using Apilane.Api.Core.Enums;
using Apilane.Common.Utilities;

namespace Apilane.UnitTests
{
    [TestClass]
    public class EnumProviderTests
    {
        // ─── Parse ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void Parse_ValidEnumName_ReturnsEnumValue()
        {
            var result = EnumProvider<AppErrors>.Parse("ERROR");
            Assert.AreEqual(AppErrors.ERROR, result);
        }

        [TestMethod]
        public void Parse_CaseInsensitive_ReturnsEnumValue()
        {
            var result = EnumProvider<AppErrors>.Parse("error");
            Assert.AreEqual(AppErrors.ERROR, result);
        }

        [TestMethod]
        public void Parse_MixedCase_ReturnsEnumValue()
        {
            var result = EnumProvider<AppErrors>.Parse("Error");
            Assert.AreEqual(AppErrors.ERROR, result);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Parse_InvalidName_ThrowsArgumentException()
        {
            EnumProvider<AppErrors>.Parse("DOES_NOT_EXIST");
        }

        // ─── GetDisplayValue ─────────────────────────────────────────────────────

        [TestMethod]
        public void GetDisplayValue_Error_ReturnsSomethingWentWrong()
        {
            var result = EnumProvider<AppErrors>.GetDisplayValue(AppErrors.ERROR);
            Assert.AreEqual("Something went wrong", result);
        }

        [TestMethod]
        public void GetDisplayValue_Unauthorized_ReturnsUnauthorized()
        {
            var result = EnumProvider<AppErrors>.GetDisplayValue(AppErrors.UNAUTHORIZED);
            Assert.AreEqual("Unauthorized", result);
        }

        [TestMethod]
        public void GetDisplayValue_NotFound_ReturnsNotFound()
        {
            var result = EnumProvider<AppErrors>.GetDisplayValue(AppErrors.NOT_FOUND);
            Assert.AreEqual("Not found", result);
        }

        [TestMethod]
        public void GetDisplayValue_Required_ReturnsRequired()
        {
            var result = EnumProvider<AppErrors>.GetDisplayValue(AppErrors.REQUIRED);
            Assert.AreEqual("Required", result);
        }
    }
}
