using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class GroupDataTryParseTypeTests
    {
        // ─── TryParseType: accepted suffixes ────────────────────────────────────

        [TestMethod]
        [DataRow("year", GroupData.GroupByType.Date_Year)]
        [DataRow("month", GroupData.GroupByType.Date_Month)]
        [DataRow("day", GroupData.GroupByType.Date_Day)]
        [DataRow("hour", GroupData.GroupByType.Date_Hour)]
        [DataRow("minute", GroupData.GroupByType.Date_Minute)]
        [DataRow("second", GroupData.GroupByType.Date_Second)]
        public void TryParseType_KnownSuffix_ReturnsTrueAndType(string text, GroupData.GroupByType expected)
        {
            var result = GroupData.TryParseType(text, out var type);

            Assert.IsTrue(result);
            Assert.AreEqual(expected, type);
        }

        [TestMethod]
        [DataRow("YEAR", GroupData.GroupByType.Date_Year)]
        [DataRow("Month", GroupData.GroupByType.Date_Month)]
        [DataRow("  day  ", GroupData.GroupByType.Date_Day)]
        [DataRow("\tHoUr\t", GroupData.GroupByType.Date_Hour)]
        [DataRow(" MINUTE", GroupData.GroupByType.Date_Minute)]
        [DataRow("second ", GroupData.GroupByType.Date_Second)]
        public void TryParseType_MixedCaseOrWhitespace_ReturnsTrueAndType(string text, GroupData.GroupByType expected)
        {
            var result = GroupData.TryParseType(text, out var type);

            Assert.IsTrue(result);
            Assert.AreEqual(expected, type);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(" ")]
        [DataRow("\t")]
        [DataRow(null)]
        public void TryParseType_EmptyOrWhitespace_ReturnsTrueAndNone(string? text)
        {
            var result = GroupData.TryParseType(text, out var type);

            Assert.IsTrue(result);
            Assert.AreEqual(GroupData.GroupByType.None, type);
        }

        // ─── TryParseType: rejected suffixes ────────────────────────────────────

        [TestMethod]
        [DataRow("foo")]
        [DataRow("none")]
        [DataRow("Date_Year")]
        [DataRow("years")]
        [DataRow("yea r")]
        [DataRow("x from users--")]
        [DataRow("x FROM Users UNION SELECT Password FROM Users--")]
        [DataRow("year--")]
        [DataRow("year)")]
        [DataRow("year;")]
        [DataRow("year AS x")]
        [DataRow("\"")]
        [DataRow("]")]
        [DataRow("`")]
        [DataRow("'")]
        [DataRow("year\"")]
        [DataRow("year]")]
        [DataRow("year`")]
        public void TryParseType_UnknownOrInjectionText_ReturnsFalseAndNone(string text)
        {
            var result = GroupData.TryParseType(text, out var type);

            Assert.IsFalse(result);
            Assert.AreEqual(GroupData.GroupByType.None, type);
        }

        // ─── GetTypeSuffix / GetAlias ───────────────────────────────────────────

        [TestMethod]
        [DataRow(GroupData.GroupByType.Date_Year)]
        [DataRow(GroupData.GroupByType.Date_Month)]
        [DataRow(GroupData.GroupByType.Date_Day)]
        [DataRow(GroupData.GroupByType.Date_Hour)]
        [DataRow(GroupData.GroupByType.Date_Minute)]
        [DataRow(GroupData.GroupByType.Date_Second)]
        [DataRow(GroupData.GroupByType.None)]
        public void GetTypeSuffix_EveryType_RoundTripsThroughTryParseType(GroupData.GroupByType type)
        {
            var suffix = GroupData.GetTypeSuffix(type);

            Assert.IsTrue(GroupData.TryParseType(suffix, out var parsed));
            Assert.AreEqual(type, parsed);
        }

        [TestMethod]
        [DataRow("Created", GroupData.GroupByType.Date_Year, "Created_year")]
        [DataRow("Created", GroupData.GroupByType.Date_Month, "Created_month")]
        [DataRow("Created", GroupData.GroupByType.Date_Day, "Created_day")]
        [DataRow("Created", GroupData.GroupByType.Date_Hour, "Created_hour")]
        [DataRow("Created", GroupData.GroupByType.Date_Minute, "Created_minute")]
        [DataRow("Created", GroupData.GroupByType.Date_Second, "Created_second")]
        [DataRow("Custom_Integer", GroupData.GroupByType.None, "Custom_Integer")]
        public void GetAlias_PropertyAndType_ReturnsCanonicalAlias(string propertyName, GroupData.GroupByType type, string expected)
        {
            Assert.AreEqual(expected, GroupData.GetAlias(propertyName, type));
        }

        [TestMethod]
        [DataRow("year")]
        [DataRow("YEAR")]
        [DataRow("Month")]
        [DataRow("day")]
        [DataRow("HOUR")]
        [DataRow("minute")]
        [DataRow("Second")]
        public void GetAlias_ParsedSuffix_MatchesLegacyAliasFormat(string suffix)
        {
            // The alias used to be built as $"{Name}_{suffix.ToLower()}"; clients read these keys, so they must not change.
            var legacyAlias = $"Created_{suffix.ToLowerInvariant()}";

            Assert.IsTrue(GroupData.TryParseType(suffix, out var type));
            Assert.AreEqual(legacyAlias, GroupData.GetAlias("Created", type));
        }

        // ─── ConvertToType (lenient, kept for compatibility) ────────────────────

        [TestMethod]
        [DataRow("x from users--")]
        [DataRow("year--")]
        public void ConvertToType_InjectionText_ReturnsNone(string text)
        {
            Assert.AreEqual(GroupData.GroupByType.None, GroupData.ConvertToType(text));
        }
    }
}
