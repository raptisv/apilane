using Apilane.Common.Extensions;
using System.Text.Json.Nodes;

namespace Apilane.UnitTests
{
    [TestClass]
    public class JsonObjectExtensionsTests
    {
        // ─── GetObjectProperty ───────────────────────────────────────────────────

        [TestMethod]
        public void GetObjectProperty_ExistingKey_ReturnsValue()
        {
            var obj = System.Text.Json.JsonSerializer.Deserialize<JsonObject>("{\"Name\":\"Alice\"}");
            Assert.IsNotNull(obj);
            var result = obj!.GetObjectProperty("Name");
            Assert.AreEqual("Alice", result);
        }

        [TestMethod]
        public void GetObjectProperty_MissingKey_ReturnsNull()
        {
            var obj = System.Text.Json.JsonSerializer.Deserialize<JsonObject>("{\"Name\":\"Alice\"}");
            Assert.IsNotNull(obj);
            var result = obj!.GetObjectProperty("Age");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetObjectProperty_EmptyObject_ReturnsNull()
        {
            var obj = System.Text.Json.JsonSerializer.Deserialize<JsonObject>("{}");
            Assert.IsNotNull(obj);
            var result = obj!.GetObjectProperty("Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetObjectProperty_NullValue_ReturnsEmptyString()
        {
            var obj = System.Text.Json.JsonSerializer.Deserialize<JsonObject>("{\"Name\":null}");
            Assert.IsNotNull(obj);
            // Utils.GetString(null) returns ""
            var result = obj!.GetObjectProperty("Name");
            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void GetObjectProperty_IsCaseSensitive()
        {
            var obj = System.Text.Json.JsonSerializer.Deserialize<JsonObject>("{\"Name\":\"Bob\"}");
            Assert.IsNotNull(obj);
            var result = obj!.GetObjectProperty("name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetObjectProperty_NumericValue_ReturnsStringRepresentation()
        {
            var obj = System.Text.Json.JsonSerializer.Deserialize<JsonObject>("{\"Age\":42}");
            Assert.IsNotNull(obj);
            var result = obj!.GetObjectProperty("Age");
            Assert.AreEqual("42", result);
        }
    }
}
