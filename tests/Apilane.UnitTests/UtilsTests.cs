using Apilane.Common;
using Utils = Apilane.Common.Utils;

namespace Apilane.UnitTests
{
    [TestClass]
    public class UtilsTests
    {
        // ─── GetString ─────────────────────────────────────────────────────────

        [TestMethod]
        public void GetString_Null_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, Utils.GetString(null));
        }

        [TestMethod]
        public void GetString_StringValue_ReturnsTrimmedString()
        {
            Assert.AreEqual("hello", Utils.GetString("  hello  "));
        }

        [TestMethod]
        public void GetString_IntegerObject_ReturnsStringRepresentation()
        {
            Assert.AreEqual("42", Utils.GetString(42));
        }

        // ─── GetInt ────────────────────────────────────────────────────────────

        [TestMethod]
        public void GetInt_ValidString_ReturnsInt()
        {
            Assert.AreEqual(5, Utils.GetInt("5"));
        }

        [TestMethod]
        public void GetInt_Null_ReturnsDefault()
        {
            Assert.AreEqual(-1, Utils.GetInt(null));
        }

        [TestMethod]
        public void GetInt_InvalidString_ReturnsDefault()
        {
            Assert.AreEqual(-1, Utils.GetInt("not-a-number"));
        }

        [TestMethod]
        public void GetInt_CustomDefault_ReturnsCustomDefault()
        {
            Assert.AreEqual(99, Utils.GetInt(null, 99));
        }

        // ─── GetLong ───────────────────────────────────────────────────────────

        [TestMethod]
        public void GetLong_ValidString_ReturnsLong()
        {
            Assert.AreEqual(9999999999L, Utils.GetLong("9999999999"));
        }

        [TestMethod]
        public void GetLong_Null_ReturnsDefault()
        {
            Assert.AreEqual(-1L, Utils.GetLong(null));
        }

        [TestMethod]
        public void GetLong_InvalidString_ReturnsDefault()
        {
            Assert.AreEqual(-1L, Utils.GetLong("abc"));
        }

        // ─── GetBool ───────────────────────────────────────────────────────────

        [TestMethod]
        public void GetBool_True_ReturnsTrue()
        {
            Assert.IsTrue(Utils.GetBool("true"));
            Assert.IsTrue(Utils.GetBool("1"));
            Assert.IsTrue(Utils.GetBool("on"));
            Assert.IsTrue(Utils.GetBool("True"));
            Assert.IsTrue(Utils.GetBool("TRUE"));
        }

        [TestMethod]
        public void GetBool_False_ReturnsFalse()
        {
            Assert.IsFalse(Utils.GetBool("false"));
            Assert.IsFalse(Utils.GetBool("0"));
            Assert.IsFalse(Utils.GetBool("off"));
        }

        [TestMethod]
        public void GetBool_Null_ReturnsDefault()
        {
            Assert.IsFalse(Utils.GetBool(null));
            Assert.IsTrue(Utils.GetBool(null, true));
        }

        [TestMethod]
        public void GetBool_InvalidString_ReturnsDefault()
        {
            Assert.IsFalse(Utils.GetBool("gibberish"));
        }

        // ─── GetFloat ──────────────────────────────────────────────────────────

        [TestMethod]
        public void GetFloat_ValidCommaDecimal_ParsesCorrectly()
        {
            Assert.AreEqual(3.14f, Utils.GetFloat("3,14"), delta: 0.001f);
        }

        [TestMethod]
        public void GetFloat_ValidDotDecimal_ParsesCorrectly()
        {
            Assert.AreEqual(3.14f, Utils.GetFloat("3.14"), delta: 0.001f);
        }

        // ─── GetDouble ─────────────────────────────────────────────────────────

        [TestMethod]
        public void GetDouble_ValidString_ReturnsDouble()
        {
            Assert.AreEqual(1.5, Utils.GetDouble("1.5"), delta: 0.0001);
        }

        [TestMethod]
        public void GetDouble_Null_ReturnsDefault()
        {
            Assert.AreEqual(-1.0, Utils.GetDouble(null), delta: 0.0001);
        }

        // ─── GetDecimal ────────────────────────────────────────────────────────

        [TestMethod]
        public void GetDecimal_ValidString_ReturnsDecimal()
        {
            Assert.AreEqual(9.99m, Utils.GetDecimal("9.99"));
        }

        [TestMethod]
        public void GetDecimal_InvalidString_ReturnsDefault()
        {
            Assert.AreEqual(-1m, Utils.GetDecimal("bad"));
        }

        // ─── Truncate ──────────────────────────────────────────────────────────

        [TestMethod]
        public void Truncate_PositiveValue_TruncatesCorrectly()
        {
            decimal value = 1.456m;
            Assert.AreEqual(1.45m, value.Truncate(2));
        }

        [TestMethod]
        public void Truncate_NegativeValue_TruncatesCorrectly()
        {
            decimal value = -1.456m;
            Assert.AreEqual(-1.45m, value.Truncate(2));
        }

        [TestMethod]
        public void Truncate_ZeroDecimals_TruncatesToWholeNumber()
        {
            decimal value = 3.9m;
            Assert.AreEqual(3m, value.Truncate(0));
        }

        // ─── ValidateIPv4 ──────────────────────────────────────────────────────

        [TestMethod]
        public void ValidateIPv4_ValidAddress_ReturnsTrue()
        {
            Assert.IsTrue(Utils.ValidateIPv4("192.168.1.1"));
            Assert.IsTrue(Utils.ValidateIPv4("0.0.0.0"));
            Assert.IsTrue(Utils.ValidateIPv4("255.255.255.255"));
        }

        [TestMethod]
        public void ValidateIPv4_InvalidAddress_ReturnsFalse()
        {
            Assert.IsFalse(Utils.ValidateIPv4("256.0.0.1"));       // out of range
            Assert.IsFalse(Utils.ValidateIPv4("192.168.1"));        // missing octet
            Assert.IsFalse(Utils.ValidateIPv4("not.an.ip.address"));
            Assert.IsFalse(Utils.ValidateIPv4(""));
            Assert.IsFalse(Utils.ValidateIPv4("   "));
        }

        // ─── IsValidEmail ──────────────────────────────────────────────────────

        [TestMethod]
        public void IsValidEmail_ValidAddresses_ReturnsTrue()
        {
            Assert.IsTrue(Utils.IsValidEmail("user@example.com"));
            Assert.IsTrue(Utils.IsValidEmail("user.name+tag@domain.co.uk"));
        }

        [TestMethod]
        public void IsValidEmail_InvalidAddresses_ReturnsFalse()
        {
            Assert.IsFalse(Utils.IsValidEmail(null));
            Assert.IsFalse(Utils.IsValidEmail(""));
            Assert.IsFalse(Utils.IsValidEmail("not-an-email"));
            Assert.IsFalse(Utils.IsValidEmail("@nodomain.com"));
            Assert.IsFalse(Utils.IsValidEmail("user@"));
        }

        // ─── IsValidRegex ──────────────────────────────────────────────────────

        [TestMethod]
        public void IsValidRegex_ValidPattern_ReturnsTrue()
        {
            Assert.IsTrue(Utils.IsValidRegex(@"^\d+$"));
            Assert.IsTrue(Utils.IsValidRegex(@"[a-zA-Z]+"));
        }

        [TestMethod]
        public void IsValidRegex_InvalidPattern_ReturnsFalse()
        {
            Assert.IsFalse(Utils.IsValidRegex("[invalid"));
            Assert.IsFalse(Utils.IsValidRegex(""));
        }

        // ─── IsValidRegexMatch ─────────────────────────────────────────────────

        [TestMethod]
        public void IsValidRegexMatch_MatchingInput_ReturnsTrue()
        {
            Assert.IsTrue(Utils.IsValidRegexMatch("12345", @"^\d+$"));
        }

        [TestMethod]
        public void IsValidRegexMatch_NonMatchingInput_ReturnsFalse()
        {
            Assert.IsFalse(Utils.IsValidRegexMatch("hello", @"^\d+$"));
        }

        [TestMethod]
        public void IsValidRegexMatch_EmptyRegex_ReturnsFalse()
        {
            Assert.IsFalse(Utils.IsValidRegexMatch("anything", ""));
        }

        // ─── ParseDate ─────────────────────────────────────────────────────────

        [TestMethod]
        public void ParseDate_UnixTimestampSeconds_ReturnsCorrectDate()
        {
            // Unix epoch second 0 → 1970-01-01
            var result = Utils.ParseDate("0000000000"); // 10 chars — but 0 is invalid (< 0 guard), use known value
            // Use 1609459200 = 2021-01-01 00:00:00 UTC
            var result2 = Utils.ParseDate("1609459200");
            Assert.IsNotNull(result2);
            Assert.AreEqual(2021, result2!.Value.Year);
            Assert.AreEqual(1, result2.Value.Month);
            Assert.AreEqual(1, result2.Value.Day);
        }

        [TestMethod]
        public void ParseDate_UnixTimestampMilliseconds_ReturnsCorrectDate()
        {
            // 1609459200000 ms = 2021-01-01
            var result = Utils.ParseDate("1609459200000");
            Assert.IsNotNull(result);
            Assert.AreEqual(2021, result!.Value.Year);
        }

        [TestMethod]
        public void ParseDate_YearMonthDay_ReturnsCorrectDate()
        {
            var result = Utils.ParseDate("2024-06-15");
            Assert.IsNotNull(result);
            Assert.AreEqual(2024, result!.Value.Year);
            Assert.AreEqual(6, result.Value.Month);
            Assert.AreEqual(15, result.Value.Day);
        }

        [TestMethod]
        public void ParseDate_FullDateTimeWithMs_ReturnsCorrectDate()
        {
            var result = Utils.ParseDate("2024-06-15 10:30:45.123");
            Assert.IsNotNull(result);
            Assert.AreEqual(2024, result!.Value.Year);
            Assert.AreEqual(10, result.Value.Hour);
            Assert.AreEqual(30, result.Value.Minute);
        }

        [TestMethod]
        public void ParseDate_InvalidInput_ReturnsNull()
        {
            Assert.IsNull(Utils.ParseDate("not-a-date"));
            Assert.IsNull(Utils.ParseDate(""));
        }

        // ─── GetUnixTimestampMilliseconds ──────────────────────────────────────

        [TestMethod]
        public void GetUnixTimestampMilliseconds_Epoch_ReturnsZero()
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(0L, Utils.GetUnixTimestampMilliseconds(epoch));
        }

        [TestMethod]
        public void GetUnixTimestampMilliseconds_KnownDate_ReturnsCorrectMs()
        {
            // 2021-01-01 00:00:00 UTC = 1609459200000 ms
            var date = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(1609459200000L, Utils.GetUnixTimestampMilliseconds(date));
        }
    }
}
