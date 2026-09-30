using Apilane.Common.Enums;
using Apilane.Data.Utilities;

namespace Apilane.UnitTests
{
    [TestClass]
    public class SqlUtilisTests
    {
        // ─── QuoteIdentifier ────────────────────────────────────────────────────

        [TestMethod]
        [DataRow(DatabaseType.SQLLite, "\"Created_year\"")]
        [DataRow(DatabaseType.SQLServer, "[Created_year]")]
        [DataRow(DatabaseType.MySQL, "`Created_year`")]
        [DataRow(DatabaseType.PostgreSQL, "\"Created_year\"")]
        public void QuoteIdentifier_PlainAlias_QuotesPerDatabase(DatabaseType databaseType, string expected)
        {
            Assert.AreEqual(expected, SqlUtilis.QuoteIdentifier("Created_year", databaseType));
        }

        [TestMethod]
        [DataRow(DatabaseType.SQLLite, "a\"b", "\"a\"\"b\"")]
        [DataRow(DatabaseType.SQLServer, "a]b", "[a]]b]")]
        [DataRow(DatabaseType.MySQL, "a`b", "`a``b`")]
        [DataRow(DatabaseType.PostgreSQL, "a\"b", "\"a\"\"b\"")]
        public void QuoteIdentifier_EmbeddedClosingQuote_IsDoubled(DatabaseType databaseType, string identifier, string expected)
        {
            Assert.AreEqual(expected, SqlUtilis.QuoteIdentifier(identifier, databaseType));
        }

        [TestMethod]
        [DataRow(DatabaseType.SQLLite, "x\" FROM Users--", "\"x\"\" FROM Users--\"")]
        [DataRow(DatabaseType.SQLServer, "x] FROM Users--", "[x]] FROM Users--]")]
        [DataRow(DatabaseType.MySQL, "x` FROM Users--", "`x`` FROM Users--`")]
        [DataRow(DatabaseType.PostgreSQL, "x\" FROM Users--", "\"x\"\" FROM Users--\"")]
        public void QuoteIdentifier_BreakoutAttempt_StaysInsideIdentifier(DatabaseType databaseType, string identifier, string expected)
        {
            Assert.AreEqual(expected, SqlUtilis.QuoteIdentifier(identifier, databaseType));
        }

        [TestMethod]
        public void QuoteIdentifier_UnknownDatabaseType_Throws()
        {
            Assert.ThrowsExactly<NotSupportedException>(() => SqlUtilis.QuoteIdentifier("Created_year", (DatabaseType)99));
        }
    }
}
