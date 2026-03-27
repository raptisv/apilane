using Apilane.Common.Utilities;

namespace Apilane.UnitTests
{
    [TestClass]
    public class EncryptorTests
    {
        private const string Key = "dbws_!_@"; // 8-byte DES key (same as Globals.EncryptionKey)

        [TestMethod]
        public void Encrypt_Decrypt_RoundTrip_ReturnsOriginal()
        {
            var original = "hello world";

            var encrypted = Encryptor.Encrypt(original, Key);
            var decrypted = Encryptor.Decrypt(encrypted, Key);

            Assert.AreEqual(original, decrypted);
        }

        [TestMethod]
        public void Encrypt_ProducesDifferentOutputFromInput()
        {
            var original = "plaintext";

            var encrypted = Encryptor.Encrypt(original, Key);

            Assert.AreNotEqual(original, encrypted);
        }

        [TestMethod]
        public void Encrypt_IsDeterministic_SameInputSameKey()
        {
            var input = "deterministic-test";

            var e1 = Encryptor.Encrypt(input, Key);
            var e2 = Encryptor.Encrypt(input, Key);

            Assert.AreEqual(e1, e2);
        }

        [TestMethod]
        public void Decrypt_NullOrEmpty_ReturnsEmptyOrSameString()
        {
            var resultNull = Encryptor.Decrypt(null, Key);
            var resultEmpty = Encryptor.Decrypt("", Key);

            Assert.AreEqual(string.Empty, resultNull);
            Assert.AreEqual(string.Empty, resultEmpty);
        }

        [TestMethod]
        public void Decrypt_WhitespaceOnly_ReturnsSameString()
        {
            var result = Encryptor.Decrypt("   ", Key);

            // IsNullOrWhiteSpace → returns the input as-is
            Assert.AreEqual("   ", result);
        }

        [TestMethod]
        public void TryDecrypt_ValidCiphertext_ReturnsTrueAndDecryptedValue()
        {
            var original = "secret value";
            var encrypted = Encryptor.Encrypt(original, Key);

            var success = Encryptor.TryDecrypt(encrypted, Key, out var output);

            Assert.IsTrue(success);
            Assert.AreEqual(original, output);
        }

        [TestMethod]
        public void TryDecrypt_InvalidCiphertext_ReturnsFalseAndNullOutput()
        {
            var success = Encryptor.TryDecrypt("this-is-not-valid-base64-ciphertext!!!", Key, out var output);

            Assert.IsFalse(success);
            Assert.IsNull(output);
        }

        [TestMethod]
        public void TryDecrypt_Null_ReturnsTrueAndEmptyString()
        {
            // null/empty goes through the IsNullOrWhiteSpace guard and returns early
            var success = Encryptor.TryDecrypt(null, Key, out var output);

            Assert.IsTrue(success);
            Assert.AreEqual(string.Empty, output);
        }

        [TestMethod]
        public void Encrypt_Decrypt_SpecialCharacters_RoundTrip()
        {
            var original = "!@#$%^&*()_+-=[]{}|;':\",./<>?`~";

            var encrypted = Encryptor.Encrypt(original, Key);
            var decrypted = Encryptor.Decrypt(encrypted, Key);

            Assert.AreEqual(original, decrypted);
        }

        [TestMethod]
        public void Encrypt_Decrypt_LongString_RoundTrip()
        {
            var original = new string('A', 500);

            var encrypted = Encryptor.Encrypt(original, Key);
            var decrypted = Encryptor.Decrypt(encrypted, Key);

            Assert.AreEqual(original, decrypted);
        }
    }
}
