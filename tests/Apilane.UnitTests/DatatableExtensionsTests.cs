using Apilane.Common.Extensions;
using System.Data;

namespace Apilane.UnitTests
{
    [TestClass]
    public class DatatableExtensionsTests
    {
        // ─── DataRow.ToDictionary ──────────────────────────────────────────────

        [TestMethod]
        public void DataRow_ToDictionary_ReturnsCorrectKeyValuePairs()
        {
            var table = new DataTable();
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Age", typeof(int));
            var row = table.NewRow();
            row["Name"] = "Alice";
            row["Age"] = 30;
            table.Rows.Add(row);

            var dict = table.Rows[0].ToDictionary();

            Assert.AreEqual("Alice", dict["Name"]);
            Assert.AreEqual(30, dict["Age"]);
        }

        [TestMethod]
        public void DataRow_ToDictionary_DBNull_BecomesNull()
        {
            var table = new DataTable();
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Optional", typeof(string));
            var row = table.NewRow();
            row["Name"] = "Bob";
            row["Optional"] = DBNull.Value;
            table.Rows.Add(row);

            var dict = table.Rows[0].ToDictionary();

            Assert.IsNull(dict["Optional"]);
        }

        [TestMethod]
        public void DataRow_ToDictionary_AllColumnsPresent()
        {
            var table = new DataTable();
            table.Columns.Add("A");
            table.Columns.Add("B");
            table.Columns.Add("C");
            var row = table.NewRow();
            row["A"] = "1"; row["B"] = "2"; row["C"] = "3";
            table.Rows.Add(row);

            var dict = table.Rows[0].ToDictionary();

            Assert.AreEqual(3, dict.Count);
            Assert.IsTrue(dict.ContainsKey("A"));
            Assert.IsTrue(dict.ContainsKey("B"));
            Assert.IsTrue(dict.ContainsKey("C"));
        }

        // ─── DataTable.ToDictionary ────────────────────────────────────────────

        [TestMethod]
        public void DataTable_ToDictionary_ReturnsOneEntryPerRow()
        {
            var table = new DataTable();
            table.Columns.Add("ID", typeof(int));
            table.Rows.Add(table.NewRow());
            table.Rows[0]["ID"] = 1;
            table.Rows.Add(table.NewRow());
            table.Rows[1]["ID"] = 2;

            var result = table.ToDictionary();

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result[0]["ID"]);
            Assert.AreEqual(2, result[1]["ID"]);
        }

        [TestMethod]
        public void DataTable_ToDictionary_EmptyTable_ReturnsEmptyList()
        {
            var table = new DataTable();
            table.Columns.Add("ID");

            var result = table.ToDictionary();

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void DataTable_ToDictionary_DBNullsBecomesNull()
        {
            var table = new DataTable();
            table.Columns.Add("Value", typeof(string));
            var row = table.NewRow();
            row["Value"] = DBNull.Value;
            table.Rows.Add(row);

            var result = table.ToDictionary();

            Assert.IsNull(result[0]["Value"]);
        }

        // ─── DataTableCollection.ToDictionary ─────────────────────────────────

        [TestMethod]
        public void DataTableCollection_ToDictionary_ReturnsOneListPerTable()
        {
            var dataset = new DataSet();
            var t1 = dataset.Tables.Add("T1");
            t1.Columns.Add("X");
            var r1 = t1.NewRow(); r1["X"] = "a"; t1.Rows.Add(r1);

            var t2 = dataset.Tables.Add("T2");
            t2.Columns.Add("Y");
            var r2 = t2.NewRow(); r2["Y"] = "b"; t2.Rows.Add(r2);

            var result = dataset.Tables.ToDictionary();

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("a", result[0][0]["X"]);
            Assert.AreEqual("b", result[1][0]["Y"]);
        }

        [TestMethod]
        public void DataTableCollection_ToDictionary_EmptyCollection_ReturnsEmptyList()
        {
            var dataset = new DataSet();

            var result = dataset.Tables.ToDictionary();

            Assert.AreEqual(0, result.Count);
        }
    }
}
