using Apilane.Common.Extensions;

namespace Apilane.UnitTests
{
    [TestClass]
    public class GuidExtensionsTests
    {
        [TestMethod]
        public void ToGuid_IsDeterministic()
        {
            var input = "apilane-stable-input";

            var guid1 = input.ToGuid();
            var guid2 = input.ToGuid();

            Assert.AreEqual(guid1, guid2);
        }

        [TestMethod]
        public void ToGuid_DifferentInputs_ProduceDifferentGuids()
        {
            var guid1 = "input-one".ToGuid();
            var guid2 = "input-two".ToGuid();

            Assert.AreNotEqual(guid1, guid2);
        }

        [TestMethod]
        public void ToGuid_ReturnsValidGuid()
        {
            var result = "any-string".ToGuid();

            Assert.AreNotEqual(Guid.Empty, result);
        }

        [TestMethod]
        public void ToGuid_EmptyString_ReturnsDeterministicGuid()
        {
            var guid1 = "".ToGuid();
            var guid2 = "".ToGuid();

            Assert.AreEqual(guid1, guid2);
            Assert.AreNotEqual(Guid.Empty, guid1);
        }

        [TestMethod]
        public void ToGuid_CaseSensitive()
        {
            var lower = "hello".ToGuid();
            var upper = "HELLO".ToGuid();

            Assert.AreNotEqual(lower, upper);
        }
    }
}
