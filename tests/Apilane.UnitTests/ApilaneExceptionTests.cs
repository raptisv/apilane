using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;

namespace Apilane.UnitTests
{
    [TestClass]
    public class ApilaneExceptionTests
    {
        // ─── Constructor ─────────────────────────────────────────────────────────

        [TestMethod]
        public void Constructor_SetsErrorField()
        {
            var ex = new ApilaneException(AppErrors.ERROR);
            Assert.AreEqual(AppErrors.ERROR, ex.Error);
        }

        [TestMethod]
        public void Constructor_NullMessage_UsesDisplayValueFromEnum()
        {
            var ex = new ApilaneException(AppErrors.ERROR);
            // Display attribute Name for ERROR is "Something went wrong"
            Assert.AreEqual("Something went wrong", ex.CustomMessage);
        }

        [TestMethod]
        public void Constructor_ExplicitMessage_UsesProvidedMessage()
        {
            var ex = new ApilaneException(AppErrors.ERROR, "Custom error detail");
            Assert.AreEqual("Custom error detail", ex.CustomMessage);
        }

        [TestMethod]
        public void Constructor_PropertyIsSet()
        {
            var ex = new ApilaneException(AppErrors.REQUIRED, property: "Email");
            Assert.AreEqual("Email", ex.Property);
        }

        [TestMethod]
        public void Constructor_EntityIsSet()
        {
            var ex = new ApilaneException(AppErrors.NOT_FOUND, entity: "Users");
            Assert.AreEqual("Users", ex.Entity);
        }

        [TestMethod]
        public void Constructor_AllFields_AreSet()
        {
            var ex = new ApilaneException(AppErrors.UNAUTHORIZED, "Forbidden", "Token", "AuthTokens");
            Assert.AreEqual(AppErrors.UNAUTHORIZED, ex.Error);
            Assert.AreEqual("Forbidden", ex.CustomMessage);
            Assert.AreEqual("Token", ex.Property);
            Assert.AreEqual("AuthTokens", ex.Entity);
        }

        [TestMethod]
        public void Constructor_NullPropertyAndEntity_AreNull()
        {
            var ex = new ApilaneException(AppErrors.ERROR);
            Assert.IsNull(ex.Property);
            Assert.IsNull(ex.Entity);
        }

        [TestMethod]
        public void Constructor_IsException()
        {
            var ex = new ApilaneException(AppErrors.ERROR);
            Assert.IsInstanceOfType(ex, typeof(Exception));
        }

        [TestMethod]
        public void Constructor_UnauthorizedError_DisplaysUnauthorized()
        {
            var ex = new ApilaneException(AppErrors.UNAUTHORIZED);
            Assert.AreEqual("Unauthorized", ex.CustomMessage);
        }
    }
}
