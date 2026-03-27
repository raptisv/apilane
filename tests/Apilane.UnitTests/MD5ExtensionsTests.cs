using Apilane.Common.Extensions;

namespace Apilane.UnitTests
{
    [TestClass]
    public class MD5ExtensionsTests
    {
        [TestMethod]
        public void ToMD5_KnownInput_ReturnsExpectedHash()
        {
            // MD5("hello") = 5d41402abc4b2a76b9719d911017c592
            var result = "hello".ToMD5();

            Assert.AreEqual("5d41402abc4b2a76b9719d911017c592", result);
        }

        [TestMethod]
        public void ToMD5_EmptyString_ReturnsExpectedHash()
        {
            // MD5("") = d41d8cd98f00b204e9800998ecf8427e
            var result = "".ToMD5();

            Assert.AreEqual("d41d8cd98f00b204e9800998ecf8427e", result);
        }

        [TestMethod]
        public void ToMD5_IsDeterministic()
        {
            var input = "apilane-test-string";

            var result1 = input.ToMD5();
            var result2 = input.ToMD5();

            Assert.AreEqual(result1, result2);
        }

        [TestMethod]
        public void ToMD5_DifferentInputs_ProduceDifferentHashes()
        {
            var hash1 = "foo".ToMD5();
            var hash2 = "bar".ToMD5();

            Assert.AreNotEqual(hash1, hash2);
        }

        [TestMethod]
        public void ToMD5_ReturnsLowercaseHexString()
        {
            var result = "test".ToMD5();

            Assert.AreEqual(result.ToLowerInvariant(), result);
            Assert.AreEqual(32, result.Length);
        }

        [TestMethod]
        public void ToMD5_CaseSensitive()
        {
            var lower = "hello".ToMD5();
            var upper = "HELLO".ToMD5();

            Assert.AreNotEqual(lower, upper);
        }
    }
}
