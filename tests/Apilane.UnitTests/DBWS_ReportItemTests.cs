using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class DBWS_ReportItemTests
    {
        private static DBWS_ReportItem BuildItem(
            string entity = "Orders",
            string properties = "Total,Count",
            string? filter = null,
            string groupBy = "month",
            int maxRecords = 100) => new DBWS_ReportItem
            {
                AppID = 1,
                TypeID = 1,
                Order = 0,
                PanelWidth = 12,
                Title = "Test Report",
                Entity = entity,
                Properties = properties,
                Filter = filter,
                GroupBy = groupBy,
                MaxRecords = maxRecords
            };

        // ─── GetApiUrl ───────────────────────────────────────────────────────────

        [TestMethod]
        public void GetApiUrl_ContainsEntityName()
        {
            var item = BuildItem(entity: "Orders");
            Assert.IsTrue(item.GetApiUrl().Contains("Entity=Orders"));
        }

        [TestMethod]
        public void GetApiUrl_ContainsProperties()
        {
            var item = BuildItem(properties: "Total,Count");
            Assert.IsTrue(item.GetApiUrl().Contains("Properties=Total,Count"));
        }

        [TestMethod]
        public void GetApiUrl_ContainsGroupBy()
        {
            var item = BuildItem(groupBy: "month");
            Assert.IsTrue(item.GetApiUrl().Contains("GroupBy=month"));
        }

        [TestMethod]
        public void GetApiUrl_ContainsPageSize()
        {
            var item = BuildItem(maxRecords: 50);
            Assert.IsTrue(item.GetApiUrl().Contains("PageSize=50"));
        }

        [TestMethod]
        public void GetApiUrl_NullFilter_FilterEqualsEmpty()
        {
            var item = BuildItem(filter: null);
            // Filter= appears but with no value (stripped of spaces)
            Assert.IsTrue(item.GetApiUrl().Contains("Filter="));
        }

        [TestMethod]
        public void GetApiUrl_WithFilter_FilterAppearsInUrl()
        {
            var item = BuildItem(filter: "Amount>10");
            Assert.IsTrue(item.GetApiUrl().Contains("Filter=Amount>10"));
        }

        [TestMethod]
        public void GetApiUrl_NoSpaces()
        {
            var item = BuildItem(entity: "My Entity", properties: "A, B");
            var url = item.GetApiUrl();
            Assert.IsFalse(url.Contains(' '));
        }

        [TestMethod]
        public void GetApiUrl_StartsWithStatsAggregate()
        {
            var item = BuildItem();
            Assert.IsTrue(item.GetApiUrl().StartsWith("Stats/Aggregate?"));
        }
    }
}
