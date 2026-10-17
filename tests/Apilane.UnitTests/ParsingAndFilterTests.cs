using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Data.Extensions;
using System.Diagnostics;
using System.Globalization;
using static Apilane.Common.Models.FilterData;
using Utils = Apilane.Common.Utils;

namespace Apilane.UnitTests
{
    [TestClass]
    public class ParsingAndFilterTests
    {
        private static long ToUnixMilliseconds(int year, int month, int day, int hour = 0, int minute = 0, int second = 0, int millisecond = 0)
        {
            return new DateTimeOffset(year, month, day, hour, minute, second, millisecond, TimeSpan.Zero).ToUnixTimeMilliseconds();
        }

        private static long ParseDateToUnixMilliseconds(string value)
        {
            var result = Utils.ParseDate(value);
            Assert.IsTrue(result.HasValue, $"'{value}' was not read as a date");
            return Utils.GetUnixTimestampMilliseconds(result.GetValueOrDefault());
        }

        // ─── ParseDate: Unix timestamps ─────────────────────────────────────────

        [TestMethod]
        public void ParseDate_TenDigits_Should_Be_Seconds()
        {
            Assert.AreEqual(ToUnixMilliseconds(2021, 1, 1), ParseDateToUnixMilliseconds("1609459200"));
        }

        [TestMethod]
        public void ParseDate_TwelveDigits_Should_Be_Milliseconds()
        {
            // 1990-01-01, what the API returns for a date between 1973 and 2001
            Assert.AreEqual(ToUnixMilliseconds(1990, 1, 1), ParseDateToUnixMilliseconds("631152000000"));
        }

        [TestMethod]
        public void ParseDate_ThirteenDigits_Should_Be_Milliseconds()
        {
            Assert.AreEqual(ToUnixMilliseconds(2021, 1, 1, 10, 30, 45, 123), ParseDateToUnixMilliseconds("1609497045123"));
        }

        [TestMethod]
        [DataRow("-631152000000")]
        [DataRow("-1")]
        [DataRow("-1000000000")]
        public void ParseDate_Negative_Should_Be_Milliseconds(string value)
        {
            // A date before 1970, as the API returns it; a negative value is never read as seconds
            Assert.AreEqual(long.Parse(value, CultureInfo.InvariantCulture), ParseDateToUnixMilliseconds(value));
        }

        [TestMethod]
        public void ParseDate_NegativeMilliseconds_Should_Be_The_Date_Before_1970()
        {
            Assert.AreEqual(ToUnixMilliseconds(1950, 1, 1), ParseDateToUnixMilliseconds("-631152000000"));
        }

        [TestMethod]
        [DataRow("-")]
        [DataRow("12a")]
        [DataRow("+1609459200")]
        [DataRow("99999999999999999")]
        [DataRow("-99999999999999999")]
        [DataRow("99999999999999999999999")]
        public void ParseDate_InvalidOrOutOfRangeNumber_Should_Be_Null(string value)
        {
            Assert.IsNull(Utils.ParseDate(value));
        }

        // ─── ParseDate: text formats ────────────────────────────────────────────

        [TestMethod]
        public void ParseDate_YearMonthDay_Should_Be_Midnight()
        {
            Assert.AreEqual(ToUnixMilliseconds(1990, 1, 1), ParseDateToUnixMilliseconds("1990-01-01"));
        }

        [TestMethod]
        [DataRow("2026-01-02T03:04", 0, 0)]
        [DataRow("2026-01-02T03:04:05", 5, 0)]
        [DataRow("2026-01-02T03:04:05Z", 5, 0)]
        [DataRow("2026-01-02T03:04:05.123", 5, 123)]
        [DataRow("2026-01-02T03:04:05.123Z", 5, 123)]
        [DataRow("2026-01-02T03:04:05.1234567Z", 5, 123)]
        [DataRow("2026-01-02T03:04:05.5Z", 5, 500)]
        [DataRow("2026-01-02T05:04:05+02:00", 5, 0)]
        [DataRow("2026-01-02T05:04:05.123+02:00", 5, 123)]
        [DataRow("2026-01-01T22:04:05-05:00", 5, 0)]
        public void ParseDate_IsoWithT_Should_Be_Read_As_Utc(string value, int second, int millisecond)
        {
            // Without a zone the value is UTC, like the other text formats; with an offset it is converted to UTC
            Assert.AreEqual(ToUnixMilliseconds(2026, 1, 2, 3, 4, second, millisecond), ParseDateToUnixMilliseconds(value));
        }

        [TestMethod]
        public void ParseDate_IsoWithZone_Should_Return_Utc_Kind()
        {
            var result = Utils.ParseDate("2026-01-02T05:04:05+02:00");
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(DateTimeKind.Utc, result.GetValueOrDefault().Kind);
            Assert.AreEqual(3, result.GetValueOrDefault().Hour);
        }

        [TestMethod]
        [DataRow("2026-01-02T")]
        [DataRow("2026-01-02T03")]
        [DataRow("2026-13-02T03:04:05Z")]
        [DataRow("2026-01-02T03:04:05ZZ")]
        public void ParseDate_InvalidIso_Should_Be_Null(string value)
        {
            Assert.IsNull(Utils.ParseDate(value));
        }

        // ─── IsValidRegexMatch: timeout ─────────────────────────────────────────

        [TestMethod]
        public void IsValidRegexMatch_CatastrophicBacktracking_Should_Time_Out_As_No_Match()
        {
            // Backtracks exponentially without a match timeout (minutes for this input)
            var stopwatch = Stopwatch.StartNew();
            var result = Utils.IsValidRegexMatch(new string('a', 40) + "!", @"^(\w+\s?)*$");
            stopwatch.Stop();

            Assert.IsFalse(result);
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"The match took {stopwatch.Elapsed}");
        }

        [TestMethod]
        public void IsValidRegexMatch_SamePatternOnMatchingValue_Should_Match()
        {
            Assert.IsTrue(Utils.IsValidRegexMatch("hello big world", @"^(\w+\s?)*$"));
        }

        // ─── Filter SQL: numbers under another culture ──────────────────────────

        private static string ToSql(string property, FilterOperators oper, object? value, PropertyType type)
        {
            return new FilterData(property, oper, value, type).ToSqlExpression("Orders", DatabaseType.SQLServer);
        }

        [TestMethod]
        public void ToSqlExpression_Numbers_Should_Be_Invariant_Under_Another_Culture()
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                // el-GR writes and reads ',' as the decimal separator and '.' as the group separator
                CultureInfo.CurrentCulture = new CultureInfo("el-GR");

                Assert.AreEqual("[Orders].[Price]=-5.5", ToSql("Price", FilterOperators.equal, -5.5m, PropertyType.Number));
                Assert.AreEqual("[Orders].[Price]>3.5", ToSql("Price", FilterOperators.greater, "3.5", PropertyType.Number));
                Assert.AreEqual("[Orders].[Price]<=1000", ToSql("Price", FilterOperators.lessorequal, "1e3", PropertyType.Number));
                Assert.AreEqual("[Orders].[Price] IN (1.5,-2,3)", ToSql("Price", FilterOperators.contains, " 1.5, -2 ,3 ", PropertyType.Number));
                Assert.AreEqual("[Orders].[Price] NOT IN (2.25)", ToSql("Price", FilterOperators.notcontains, 2.25m, PropertyType.Number));
                Assert.AreEqual("[Orders].[Created]<-631152000000", ToSql("Created", FilterOperators.less, -631152000000L, PropertyType.Date));
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }

        [TestMethod]
        [DataRow("3,5")]
        [DataRow("5-")]
        [DataRow("1.000,5")]
        [DataRow("(5)")]
        public void ToSqlExpression_NumberNotInvariant_Should_Throw(string value)
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("el-GR");

                Assert.ThrowsExactly<FormatException>(() => ToSql("Price", FilterOperators.equal, value, PropertyType.Number));
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }
    }
}
