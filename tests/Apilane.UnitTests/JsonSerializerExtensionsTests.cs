using Apilane.Common.Extensions;

namespace Apilane.UnitTests
{
    [TestClass]
    public class JsonSerializerExtensionsTests
    {
        private record SimpleRecord(string Name, int Age);

        // ─── DeserializeAnonymous ────────────────────────────────────────────────

        [TestMethod]
        public void DeserializeAnonymous_ValidJson_ReturnsTypedObject()
        {
            var template = new { Name = string.Empty, Age = 0 };
            var json = "{\"Name\":\"Alice\",\"Age\":30}";
            var result = json.DeserializeAnonymous(template);
            Assert.AreEqual("Alice", result.Name);
            Assert.AreEqual(30, result.Age);
        }

        [TestMethod]
        public void DeserializeAnonymous_IntValue_RoundTrip()
        {
            var template = new { Value = 0 };
            var json = "{\"Value\":42}";
            var result = json.DeserializeAnonymous(template);
            Assert.AreEqual(42, result.Value);
        }

        [TestMethod]
        public void DeserializeAnonymous_BoolValue_RoundTrip()
        {
            var template = new { Active = false };
            var json = "{\"Active\":true}";
            var result = json.DeserializeAnonymous(template);
            Assert.IsTrue(result.Active);
        }

        [TestMethod]
        public void DeserializeAnonymous_NamedRecord_RoundTrip()
        {
            var json = "{\"Name\":\"Bob\",\"Age\":25}";
            var result = json.DeserializeAnonymous(new SimpleRecord("", 0));
            Assert.AreEqual("Bob", result.Name);
            Assert.AreEqual(25, result.Age);
        }
    }
}
