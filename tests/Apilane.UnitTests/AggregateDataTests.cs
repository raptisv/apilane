using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class AggregateDataTests
    {
        // ─── ConvertToType ──────────────────────────────────────────────────────

        [TestMethod]
        public void ConvertToType_Min_ReturnsMin()
        {
            Assert.AreEqual(AggregateData.DataAggregates.Min, AggregateData.ConvertToType("min"));
        }

        [TestMethod]
        public void ConvertToType_Max_ReturnsMax()
        {
            Assert.AreEqual(AggregateData.DataAggregates.Max, AggregateData.ConvertToType("max"));
        }

        [TestMethod]
        public void ConvertToType_Sum_ReturnsSum()
        {
            Assert.AreEqual(AggregateData.DataAggregates.Sum, AggregateData.ConvertToType("sum"));
        }

        [TestMethod]
        public void ConvertToType_Avg_ReturnsAvg()
        {
            Assert.AreEqual(AggregateData.DataAggregates.Avg, AggregateData.ConvertToType("avg"));
        }

        [TestMethod]
        public void ConvertToType_Unknown_ReturnsCount()
        {
            Assert.AreEqual(AggregateData.DataAggregates.Count, AggregateData.ConvertToType("unknown"));
        }

        [TestMethod]
        public void ConvertToType_Empty_ReturnsCount()
        {
            Assert.AreEqual(AggregateData.DataAggregates.Count, AggregateData.ConvertToType(string.Empty));
        }

        [TestMethod]
        public void ConvertToType_UpperCase_ReturnsCorrectType()
        {
            Assert.AreEqual(AggregateData.DataAggregates.Min, AggregateData.ConvertToType("MIN"));
        }

        [TestMethod]
        public void ConvertToType_WithWhitespace_TrimsAndConverts()
        {
            Assert.AreEqual(AggregateData.DataAggregates.Sum, AggregateData.ConvertToType("  sum  "));
        }
    }
}
