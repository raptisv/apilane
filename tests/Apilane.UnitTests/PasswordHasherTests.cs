using Apilane.Common;
using Apilane.Common.Extensions;
using Apilane.Common.Security;
using Apilane.Common.Utilities;

namespace Apilane.UnitTests
{
    [TestClass]
    public class PasswordHasherTests
    {
        // An application key exactly as the portal stores it: 8 chars wrapped with the global key.
        private static readonly string WrappedAppKey = "12345678".Encrypt(Globals.EncryptionKey);

        [TestMethod]
        public void Hash_ProducesPrefixedFormat_AndVerifies()
        {
            var hash = PasswordHasher.Hash("correct horse battery");

            StringAssert.StartsWith(hash, PasswordHasher.Prefix);
            Assert.AreEqual(5, hash.Split('$').Length); // "", "pbkdf2-sha512", iterations, salt, hash
            Assert.IsTrue(PasswordHasher.IsHash(hash));
            Assert.IsTrue(PasswordHasher.Verify("correct horse battery", hash));
        }

        [TestMethod]
        public void Verify_WrongPassword_Fails()
        {
            var hash = PasswordHasher.Hash("correct horse battery");

            Assert.IsFalse(PasswordHasher.Verify("correct horse batter", hash));
            Assert.IsFalse(PasswordHasher.Verify("", hash));
            Assert.IsFalse(PasswordHasher.Verify(null, hash));
        }

        [TestMethod]
        public void Hash_SamePasswordTwice_ProducesDifferentValues()
        {
            var first = PasswordHasher.Hash("same password");
            var second = PasswordHasher.Hash("same password");

            Assert.AreNotEqual(first, second);
            Assert.IsTrue(PasswordHasher.Verify("same password", first));
            Assert.IsTrue(PasswordHasher.Verify("same password", second));
        }

        [TestMethod]
        public void Hash_EmbedsIterationCount_SoItCanBeRaisedLater()
        {
            var hash = PasswordHasher.Hash("password", iterations: 2_000);

            StringAssert.StartsWith(hash, PasswordHasher.Prefix + "2000$");
            Assert.IsTrue(PasswordHasher.Verify("password", hash));
        }

        [TestMethod]
        public void Hash_TooFewIterations_Throws()
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PasswordHasher.Hash("password", iterations: 10));
        }

        [TestMethod]
        public void IsHash_PlainOrLegacyValues_ReturnsFalse()
        {
            Assert.IsFalse(PasswordHasher.IsHash(null));
            Assert.IsFalse(PasswordHasher.IsHash(""));
            Assert.IsFalse(PasswordHasher.IsHash("plaintext"));
            Assert.IsFalse(PasswordHasher.IsHash(WrappedAppKey.ApplicationEncrypt("plaintext")));
        }

        [TestMethod]
        public void Verify_MalformedHash_ReturnsFalse()
        {
            Assert.IsFalse(PasswordHasher.Verify("password", PasswordHasher.Prefix));
            Assert.IsFalse(PasswordHasher.Verify("password", PasswordHasher.Prefix + "abc$salt$hash"));
            Assert.IsFalse(PasswordHasher.Verify("password", PasswordHasher.Prefix + "1000$not-base64!$hash"));
            Assert.IsFalse(PasswordHasher.Verify("password", PasswordHasher.Prefix + "1000$$"));
        }

        [TestMethod]
        public void VerifyAny_HashedValue_VerifiesWithoutRehash()
        {
            var hash = PasswordHasher.Hash("password");

            Assert.IsTrue(PasswordHasher.VerifyAny("password", hash, WrappedAppKey, out var needsRehash));
            Assert.IsFalse(needsRehash);
        }

        [TestMethod]
        public void VerifyAny_LegacyValue_VerifiesAndRequestsRehash()
        {
            // Exactly what the pre-hashing code stored in Users.Password.
            var legacy = WrappedAppKey.ApplicationEncrypt("password");

            Assert.IsTrue(PasswordHasher.VerifyAny("password", legacy, WrappedAppKey, out var needsRehash));
            Assert.IsTrue(needsRehash);
        }

        [TestMethod]
        public void VerifyAny_LegacyValueWrongPassword_Fails()
        {
            var legacy = WrappedAppKey.ApplicationEncrypt("password");

            Assert.IsFalse(PasswordHasher.VerifyAny("Password", legacy, WrappedAppKey, out var needsRehash));
            Assert.IsFalse(needsRehash);
        }

        [TestMethod]
        public void VerifyAny_MissingInputs_Fails()
        {
            var legacy = WrappedAppKey.ApplicationEncrypt("password");

            Assert.IsFalse(PasswordHasher.VerifyAny(null, legacy, WrappedAppKey, out _));
            Assert.IsFalse(PasswordHasher.VerifyAny("password", null, WrappedAppKey, out _));
            Assert.IsFalse(PasswordHasher.VerifyAny("password", "", WrappedAppKey, out _));
            Assert.IsFalse(PasswordHasher.VerifyAny("password", legacy, null, out _));
            Assert.IsFalse(PasswordHasher.VerifyAny("password", legacy, "not-a-wrapped-key", out _));
        }

        [TestMethod]
        public void IsUsersPasswordProperty_MatchesOnlyUsersPassword()
        {
            Assert.IsTrue(PasswordHasher.IsUsersPasswordProperty("Users", "Password"));
            Assert.IsTrue(PasswordHasher.IsUsersPasswordProperty("users", "password"));
            Assert.IsFalse(PasswordHasher.IsUsersPasswordProperty("Users", "Email"));
            Assert.IsFalse(PasswordHasher.IsUsersPasswordProperty("Customers", "Password"));
            Assert.IsFalse(PasswordHasher.IsUsersPasswordProperty(null, "Password"));
        }

        [TestMethod]
        public void RandomString_UsesRequestedAlphabetAndLength()
        {
            var value = Apilane.Common.Utils.RandomString(8);

            Assert.AreEqual(8, value.Length);
            Assert.IsTrue(value.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)));
            Assert.AreEqual(string.Empty, Apilane.Common.Utils.RandomString(0));
            Assert.AreNotEqual(Apilane.Common.Utils.RandomString(16), Apilane.Common.Utils.RandomString(16));
        }
    }
}
