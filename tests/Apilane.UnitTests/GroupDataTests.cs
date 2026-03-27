using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class GroupDataTests
    {
        // ─── ConvertToType ──────────────────────────────────────────────────────

        [TestMethod]
        public void ConvertToType_Year_ReturnsDateYear()
        {
            Assert.AreEqual(GroupData.GroupByType.Date_Year, GroupData.ConvertToType("year"));
        }

        [TestMethod]
        public void ConvertToType_Month_ReturnsDateMonth()
        {
            Assert.AreEqual(GroupData.GroupByType.Date_Month, GroupData.ConvertToType("month"));
        }

        [TestMethod]
        public void ConvertToType_Day_ReturnsDateDay()
        {
            Assert.AreEqual(GroupData.GroupByType.Date_Day, GroupData.ConvertToType("day"));
        }

        [TestMethod]
        public void ConvertToType_Hour_ReturnsDateHour()
        {
            Assert.AreEqual(GroupData.GroupByType.Date_Hour, GroupData.ConvertToType("hour"));
        }

        [TestMethod]
        public void ConvertToType_Minute_ReturnsDateMinute()
        {
            Assert.AreEqual(GroupData.GroupByType.Date_Minute, GroupData.ConvertToType("minute"));
        }

        [TestMethod]
        public void ConvertToType_Second_ReturnsDateSecond()
        {
            Assert.AreEqual(GroupData.GroupByType.Date_Second, GroupData.ConvertToType("second"));
        }

        [TestMethod]
        public void ConvertToType_Unknown_ReturnsNone()
        {
            Assert.AreEqual(GroupData.GroupByType.None, GroupData.ConvertToType("unknown"));
        }

        [TestMethod]
        public void ConvertToType_Empty_ReturnsNone()
        {
            Assert.AreEqual(GroupData.GroupByType.None, GroupData.ConvertToType(string.Empty));
        }

        [TestMethod]
        public void ConvertToType_UpperCase_ReturnsCorrectType()
        {
            Assert.AreEqual(GroupData.GroupByType.Date_Year, GroupData.ConvertToType("YEAR"));
        }

        [TestMethod]
        public void ConvertToType_WithWhitespace_TrimsAndConverts()
        {
            Assert.AreEqual(GroupData.GroupByType.Date_Month, GroupData.ConvertToType("  month  "));
        }
    }
}
