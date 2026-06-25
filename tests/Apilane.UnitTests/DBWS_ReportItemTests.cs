using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class DBWS_ReportItemTests
    {
        // Builds a panel + single series and returns the series' API url (the panel supplies the
        // shared Entity / GroupBy / MaxRecords; the series supplies the aggregate Property + Filter).
        private static string BuildUrl(
            string entity = "Orders",
            string property = "Total,Count",
            string? filter = null,
            string groupBy = "month",
            int maxRecords = 100)
        {
            var panel = new DBWS_ReportPanel
            {
                AppID = 1,
                TypeID = 1,
                X = 0,
                Y = 0,
                W = 12,
                H = 4,
                Title = "Test Report",
                MaxRecords = maxRecords
            };

            var series = new DBWS_ReportSeries
            {
                Label = "Series 1",
                Entity = entity,
                GroupBy = groupBy,
                Property = property,
                Filter = filter
            };

            return series.GetApiUrl(panel);
        }

        // ─── GetApiUrl ───────────────────────────────────────────────────────────

        [TestMethod]
        public void GetApiUrl_ContainsEntityName()
        {
            Assert.IsTrue(BuildUrl(entity: "Orders").Contains("Entity=Orders"));
        }

        [TestMethod]
        public void GetApiUrl_ContainsProperties()
        {
            Assert.IsTrue(BuildUrl(property: "Total,Count").Contains("Properties=Total,Count"));
        }

        [TestMethod]
        public void GetApiUrl_ContainsGroupBy()
        {
            Assert.IsTrue(BuildUrl(groupBy: "month").Contains("GroupBy=month"));
        }

        [TestMethod]
        public void GetApiUrl_ContainsPageSize()
        {
            Assert.IsTrue(BuildUrl(maxRecords: 50).Contains("PageSize=50"));
        }

        [TestMethod]
        public void GetApiUrl_NullFilter_FilterEqualsEmpty()
        {
            Assert.IsTrue(BuildUrl(filter: null).Contains("Filter="));
        }

        [TestMethod]
        public void GetApiUrl_WithFilter_FilterAppearsInUrl()
        {
            Assert.IsTrue(BuildUrl(filter: "Amount>10").Contains("Filter=Amount>10"));
        }

        [TestMethod]
        public void GetApiUrl_NoSpaces()
        {
            var url = BuildUrl(entity: "My Entity", property: "A, B");
            Assert.IsFalse(url.Contains(' '));
        }

        [TestMethod]
        public void GetApiUrl_StartsWithStatsAggregate()
        {
            Assert.IsTrue(BuildUrl().StartsWith("Stats/Aggregate?"));
        }
    }
}
