using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Data.Extensions;
using static Apilane.Common.Models.FilterData;

namespace Apilane.UnitTests
{
    [TestClass]
    public class SqlFilterDataExtensionsTests
    {
        private const string Entity = "Customers";
        private const string Property = "CustomerId";

        private static string ToSql(FilterOperators oper, object? value, DatabaseType databaseType, PropertyType type = PropertyType.String)
        {
            return new FilterData(Property, oper, value, type).ToSqlExpression(Entity, databaseType);
        }

        // ─── equal / notequal ───────────────────────────────────────────────────

        [TestMethod]
        [DataRow(DatabaseType.SQLLite, FilterOperators.equal, "[Customers].[CustomerId] like 'cust!_1' ESCAPE '!'")]
        [DataRow(DatabaseType.SQLLite, FilterOperators.notequal, "[Customers].[CustomerId] not like 'cust!_1' ESCAPE '!'")]
        [DataRow(DatabaseType.PostgreSQL, FilterOperators.equal, "\"Customers\".\"CustomerId\" ILIKE 'cust!_1' ESCAPE '!'")]
        [DataRow(DatabaseType.PostgreSQL, FilterOperators.notequal, "\"Customers\".\"CustomerId\" NOT ILIKE 'cust!_1' ESCAPE '!'")]
        [DataRow(DatabaseType.SQLServer, FilterOperators.equal, "[Customers].[CustomerId] = N'cust_1'")]
        [DataRow(DatabaseType.SQLServer, FilterOperators.notequal, "[Customers].[CustomerId] <> N'cust_1'")]
        [DataRow(DatabaseType.MySQL, FilterOperators.equal, "`Customers`.`CustomerId` = 'cust_1'")]
        [DataRow(DatabaseType.MySQL, FilterOperators.notequal, "`Customers`.`CustomerId` <> 'cust_1'")]
        public void ToSqlExpression_StringEquality_MatchesLiterally(DatabaseType databaseType, FilterOperators oper, string expected)
        {
            // SQLite/PostgreSQL need (I)LIKE for case-insensitivity so the wildcard is escaped;
            // SQL Server/MySQL collations are case-insensitive so = is used and nothing is a wildcard.
            Assert.AreEqual(expected, ToSql(oper, "cust_1", databaseType));
        }

        [TestMethod]
        [DataRow(DatabaseType.SQLLite, "[Customers].[CustomerId] like '!!!%!_[a]''\\' ESCAPE '!'")]
        [DataRow(DatabaseType.PostgreSQL, "\"Customers\".\"CustomerId\" ILIKE '!!!%!_[a]''\\' ESCAPE '!'")]
        [DataRow(DatabaseType.SQLServer, "[Customers].[CustomerId] = N'!%_[a]''\\'")]
        [DataRow(DatabaseType.MySQL, "`Customers`.`CustomerId` = '!%_[a]''\\\\'")]
        public void ToSqlExpression_StringEqual_EscapesSpecialCharacters(DatabaseType databaseType, string expected)
        {
            // '!' is the LIKE escape character, quotes are doubled, '\' is doubled inside MySQL string literals only
            Assert.AreEqual(expected, ToSql(FilterOperators.equal, "!%_[a]'\\", databaseType));
        }

        // ─── startswith / endswith / contains / notcontains ─────────────────────

        [TestMethod]
        [DataRow(FilterOperators.startswith, "[Customers].[CustomerId] like N'50!%%' ESCAPE '!'")]
        [DataRow(FilterOperators.endswith, "[Customers].[CustomerId] like N'%50!%' ESCAPE '!'")]
        [DataRow(FilterOperators.contains, "[Customers].[CustomerId] like N'%50!%%' ESCAPE '!'")]
        [DataRow(FilterOperators.notcontains, "[Customers].[CustomerId] not like N'%50!%%' ESCAPE '!'")]
        public void ToSqlExpression_StringLikeOperators_EscapeValueButKeepOperatorWildcards(FilterOperators oper, string expected)
        {
            Assert.AreEqual(expected, ToSql(oper, "50%", DatabaseType.SQLServer));
        }

        [TestMethod]
        [DataRow(DatabaseType.SQLLite, "[Customers].[CustomerId] like '%!!!%!_[a]''\\%' ESCAPE '!'")]
        [DataRow(DatabaseType.SQLServer, "[Customers].[CustomerId] like N'%!!!%!_![a]''\\%' ESCAPE '!'")]
        [DataRow(DatabaseType.MySQL, "`Customers`.`CustomerId` like '%!!!%!_[a]''\\\\%' ESCAPE '!'")]
        [DataRow(DatabaseType.PostgreSQL, "\"Customers\".\"CustomerId\" ILIKE '%!!!%!_[a]''\\%' ESCAPE '!'")]
        public void ToSqlExpression_StringContains_EscapesEveryPatternCharacter(DatabaseType databaseType, string expected)
        {
            // '[' opens a character class on SQL Server only
            Assert.AreEqual(expected, ToSql(FilterOperators.contains, "!%_[a]'\\", databaseType));
        }

        // ─── less / greater ─────────────────────────────────────────────────────

        [TestMethod]
        [DataRow(DatabaseType.SQLServer, FilterOperators.less, "[Customers].[CustomerId] < N'a_b%'")]
        [DataRow(DatabaseType.SQLServer, FilterOperators.lessorequal, "[Customers].[CustomerId] <= N'a_b%'")]
        [DataRow(DatabaseType.SQLServer, FilterOperators.greater, "[Customers].[CustomerId] > N'a_b%'")]
        [DataRow(DatabaseType.SQLServer, FilterOperators.greaterorequal, "[Customers].[CustomerId] >= N'a_b%'")]
        [DataRow(DatabaseType.MySQL, FilterOperators.less, "`Customers`.`CustomerId` < 'a_b%'")]
        [DataRow(DatabaseType.PostgreSQL, FilterOperators.greater, "\"Customers\".\"CustomerId\" > 'a_b%'")]
        [DataRow(DatabaseType.SQLLite, FilterOperators.greaterorequal, "[Customers].[CustomerId] >= 'a_b%'")]
        public void ToSqlExpression_StringComparisonOperators_AreNotEscaped(DatabaseType databaseType, FilterOperators oper, string expected)
        {
            Assert.AreEqual(expected, ToSql(oper, "a_b%", databaseType));
        }

        // ─── MySQL literals ─────────────────────────────────────────────────────

        [TestMethod]
        [DataRow(FilterOperators.equal)]
        [DataRow(FilterOperators.notequal)]
        [DataRow(FilterOperators.startswith)]
        [DataRow(FilterOperators.contains)]
        [DataRow(FilterOperators.less)]
        public void ToSqlExpression_MySqlString_HasNoNationalPrefix(FilterOperators oper)
        {
            // N'' is utf8mb3 in MySQL, which cannot hold supplementary characters such as emoji
            var sql = ToSql(oper, "x😀", DatabaseType.MySQL);

            Assert.IsFalse(sql.Contains("N'"), sql);
            StringAssert.Contains(sql, "x😀");
        }

        // ─── The filter is not mutated ──────────────────────────────────────────

        [TestMethod]
        [DataRow(DatabaseType.SQLLite)]
        [DataRow(DatabaseType.SQLServer)]
        [DataRow(DatabaseType.MySQL)]
        [DataRow(DatabaseType.PostgreSQL)]
        public void ToSqlExpression_CalledTwice_ProducesSameSqlAndKeepsValue(DatabaseType databaseType)
        {
            // Data and total-count queries convert the same filter instance
            var filter = new FilterData(FilterLogic.AND, new List<FilterData>()
            {
                new FilterData(Property, FilterOperators.equal, " O'Brien_1 ", PropertyType.String),
                new FilterData("Amount", FilterOperators.contains, "1, 2", PropertyType.Number)
            });

            var first = filter.ToSqlExpression(Entity, databaseType);
            var second = filter.ToSqlExpression(Entity, databaseType);

            Assert.AreEqual(first, second);
            StringAssert.Contains(first, "O''Brien");
            Assert.IsFalse(first.Contains("O''''Brien"), first);
            Assert.IsNotNull(filter.Filters);
            Assert.AreEqual(" O'Brien_1 ", filter.Filters[0].Value);
            Assert.AreEqual("1, 2", filter.Filters[1].Value);
        }

        // ─── Other types are unchanged ──────────────────────────────────────────

        [TestMethod]
        public void ToSqlExpression_NumberContains_IsInList()
        {
            Assert.AreEqual("[Customers].[CustomerId] IN (1,2,3)", ToSql(FilterOperators.contains, " 1, 2 ,3 ", DatabaseType.SQLServer, PropertyType.Number));
        }

        [TestMethod]
        public void ToSqlExpression_NumberInvalid_Throws()
        {
            Assert.ThrowsExactly<FormatException>(() => ToSql(FilterOperators.equal, "1 OR 1=1", DatabaseType.SQLServer, PropertyType.Number));
        }

        [TestMethod]
        [DataRow(DatabaseType.SQLServer, "ISNULL([Customers].[CustomerId], 0) = 1")]
        [DataRow(DatabaseType.PostgreSQL, "COALESCE(\"Customers\".\"CustomerId\", FALSE) = TRUE")]
        public void ToSqlExpression_Boolean_IsUnchanged(DatabaseType databaseType, string expected)
        {
            Assert.AreEqual(expected, ToSql(FilterOperators.equal, true, databaseType, PropertyType.Boolean));
        }

        [TestMethod]
        public void ToSqlExpression_NullValue_IsNullCheck()
        {
            Assert.AreEqual("[Customers].[CustomerId] IS NULL ", ToSql(FilterOperators.equal, null, DatabaseType.SQLServer));
        }
    }
}
