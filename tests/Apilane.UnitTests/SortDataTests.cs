using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class SortDataTests
    {
        // ─── ParseList ──────────────────────────────────────────────────────────

        [TestMethod]
        public void ParseList_Null_ReturnsNull()
        {
            var result = SortData.ParseList(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ParseList_Empty_ReturnsNull()
        {
            var result = SortData.ParseList(string.Empty);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ParseList_Whitespace_ReturnsNull()
        {
            var result = SortData.ParseList("   ");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ParseList_ValidSingleItem_ReturnsList()
        {
            var json = "[{\"Property\":\"Name\",\"Direction\":\"ASC\"}]";
            var result = SortData.ParseList(json)!;
            Assert.IsNotNull(result);
            var list = new System.Collections.Generic.List<SortData>(result);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("Name", list[0].Property);
            Assert.AreEqual("ASC", list[0].Direction);
        }

        [TestMethod]
        public void ParseList_ValidMultipleItems_ReturnsList()
        {
            var json = "[{\"Property\":\"Name\",\"Direction\":\"ASC\"},{\"Property\":\"Created\",\"Direction\":\"DESC\"}]";
            var result = SortData.ParseList(json)!;
            Assert.IsNotNull(result);
            var list = new System.Collections.Generic.List<SortData>(result);
            Assert.AreEqual(2, list.Count);
        }

        [TestMethod]
        public void ParseList_CaseInsensitiveKeys_ParsesCorrectly()
        {
            var json = "[{\"property\":\"Name\",\"direction\":\"ASC\"}]";
            var result = SortData.ParseList(json)!;
            Assert.IsNotNull(result);
            var list = new System.Collections.Generic.List<SortData>(result);
            Assert.AreEqual("Name", list[0].Property);
        }

        // ─── Parse ──────────────────────────────────────────────────────────────

        [TestMethod]
        public void Parse_Null_ReturnsNull()
        {
            var result = SortData.Parse(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void Parse_Empty_ReturnsNull()
        {
            var result = SortData.Parse(string.Empty);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void Parse_Whitespace_ReturnsNull()
        {
            var result = SortData.Parse("   ");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void Parse_ValidJson_ReturnsSortData()
        {
            var json = "{\"Property\":\"Age\",\"Direction\":\"DESC\"}";
            var result = SortData.Parse(json);
            Assert.IsNotNull(result);
            Assert.AreEqual("Age", result!.Property);
            Assert.AreEqual("DESC", result.Direction);
        }

        [TestMethod]
        public void Parse_CaseInsensitiveKeys_ParsesCorrectly()
        {
            var json = "{\"property\":\"Age\",\"direction\":\"DESC\"}";
            var result = SortData.Parse(json);
            Assert.IsNotNull(result);
            Assert.AreEqual("Age", result!.Property);
        }
    }
}
