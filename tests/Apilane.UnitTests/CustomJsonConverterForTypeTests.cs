using Apilane.Common.Utilities;
using System.Text.Json;

namespace Apilane.UnitTests
{
    [TestClass]
    public class CustomJsonConverterForTypeTests
    {
        // ─── Write ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void Write_WritesAssemblyQualifiedName()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new CustomJsonConverterForType());

            // Serialize a Type instance — the converter writes AssemblyQualifiedName
            var type = typeof(string);
            var json = JsonSerializer.Serialize(type, options);

            Assert.IsNotNull(json);
            // Result is a JSON string containing the assembly-qualified name
            Assert.IsTrue(json.Contains("System.String"));
        }

        [TestMethod]
        public void Write_IntType_ContainsSystemInt32()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new CustomJsonConverterForType());

            var json = JsonSerializer.Serialize(typeof(int), options);
            Assert.IsTrue(json.Contains("System.Int32"));
        }

        // ─── Read ────────────────────────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(NotSupportedException))]
        public void Read_ThrowsNotSupportedException()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new CustomJsonConverterForType());

            // Attempting to deserialize a Type should throw NotSupportedException
            JsonSerializer.Deserialize<Type>("\"System.String\"", options);
        }
    }
}
