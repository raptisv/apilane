using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class EntityConstraintExtensionsTests
    {
        // ─── GetUniqueProperties ───────────────────────────────────────────────

        [TestMethod]
        public void GetUniqueProperties_ValidUniqueConstraint_ReturnsProperties()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.Unique,
                Properties = "Email,Username"
            };

            var result = constraint.GetUniqueProperties();

            CollectionAssert.AreEquivalent(new List<string> { "Email", "Username" }, result);
        }

        [TestMethod]
        public void GetUniqueProperties_EmptyProperties_ReturnsEmptyList()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.Unique,
                Properties = ""
            };

            var result = constraint.GetUniqueProperties();

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetUniqueProperties_NullProperties_ReturnsEmptyList()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.Unique,
                Properties = null
            };

            var result = constraint.GetUniqueProperties();

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetUniqueProperties_DeduplicatesProperties()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.Unique,
                Properties = "Email,Email,Username"
            };

            var result = constraint.GetUniqueProperties();

            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void GetUniqueProperties_SortsPropertiesAlphabetically()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.Unique,
                Properties = "Username,Email"
            };

            var result = constraint.GetUniqueProperties();

            Assert.AreEqual("Email", result[0]);
            Assert.AreEqual("Username", result[1]);
        }

        [TestMethod]
        public void GetUniqueProperties_WrongConstraintType_Throws()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = "SomeColumn"
            };

            Assert.ThrowsExactly<InvalidOperationException>(() => constraint.GetUniqueProperties());
        }

        [TestMethod]
        [DataRow("Name;marker")]
        [DataRow("Name--marker")]
        [DataRow("Name]marker")]
        [DataRow("Name/*marker*/")]
        [DataRow("Name,,Other")]
        [DataRow("Name,")]
        public void GetUniqueProperties_InvalidIdentifier_ThrowsBeforeSqlCanBeBuilt(string properties)
        {
            var constraint = new EntityConstraint { TypeID = (int)ConstraintType.Unique, Properties = properties };

            Assert.ThrowsExactly<InvalidOperationException>(() => constraint.GetUniqueProperties());
        }

        [TestMethod]
        public void GetUniqueProperties_WhitespaceAndSafeLegacyNames_ReturnsIdentifiers()
        {
            var constraint = new EntityConstraint { TypeID = (int)ConstraintType.Unique, Properties = " _Name2 , ID " };

            CollectionAssert.AreEquivalent(new List<string> { "_Name2", "ID" }, constraint.GetUniqueProperties());
        }

        // ─── GetForeignKeyProperties ───────────────────────────────────────────

        [TestMethod]
        public void GetForeignKeyProperties_TwoElements_ReturnsPropAndEntityWithDefaultLogic()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = "Company_ID,Companies"
            };

            var (prop, entity, logic) = constraint.GetForeignKeyProperties();

            Assert.AreEqual("Company_ID", prop);
            Assert.AreEqual("Companies", entity);
            Assert.AreEqual(ForeignKeyLogic.ON_DELETE_NO_ACTION, logic);
        }

        [TestMethod]
        public void GetForeignKeyProperties_ThreeElements_ParsesForeignKeyLogic()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = $"Company_ID,Companies,{ForeignKeyLogic.ON_DELETE_CASCADE}"
            };

            var (prop, entity, logic) = constraint.GetForeignKeyProperties();

            Assert.AreEqual("Company_ID", prop);
            Assert.AreEqual("Companies", entity);
            Assert.AreEqual(ForeignKeyLogic.ON_DELETE_CASCADE, logic);
        }

        [TestMethod]
        public void GetForeignKeyProperties_NullProperties_Throws()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = null
            };

            Assert.ThrowsExactly<InvalidOperationException>(() => constraint.GetForeignKeyProperties());
        }

        [TestMethod]
        public void GetForeignKeyProperties_OneElement_Throws()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = "OnlyOne"
            };

            Assert.ThrowsExactly<InvalidOperationException>(() => constraint.GetForeignKeyProperties());
        }

        [TestMethod]
        public void GetForeignKeyProperties_WrongConstraintType_Throws()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.Unique,
                Properties = "Col,Table"
            };

            Assert.ThrowsExactly<InvalidOperationException>(() => constraint.GetForeignKeyProperties());
        }

        [TestMethod]
        [DataRow("Company_ID;marker,Companies")]
        [DataRow("Company_ID,Companies--marker")]
        [DataRow("Company_ID,Companies]marker")]
        [DataRow("Company_ID,,Companies")]
        [DataRow("Company_ID,Companies,")]
        [DataRow("Company_ID,Companies,7")]
        public void GetForeignKeyProperties_InvalidIdentifierOrAction_ThrowsBeforeSqlCanBeBuilt(string properties)
        {
            var constraint = new EntityConstraint { TypeID = (int)ConstraintType.ForeignKey, Properties = properties };

            Assert.ThrowsExactly<InvalidOperationException>(() => constraint.GetForeignKeyProperties());
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public void GetForeignKeyProperties_WhitespaceSafeLegacyNamesAndNumericAction_ReturnsIdentifiers(int action)
        {
            var constraint = new EntityConstraint { TypeID = (int)ConstraintType.ForeignKey, Properties = $" _Company2_ID , Companies2 , {action} " };

            var (property, entity, logic) = constraint.GetForeignKeyProperties();

            Assert.AreEqual("_Company2_ID", property);
            Assert.AreEqual("Companies2", entity);
            Assert.AreEqual((ForeignKeyLogic)action, logic);
        }

        // ─── GetForeignKeyPropertiesAsList ─────────────────────────────────────

        [TestMethod]
        public void GetForeignKeyPropertiesAsList_ValidFK_ReturnsTwoElementList()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = "Company_ID,Companies"
            };

            var result = constraint.GetForeignKeyPropertiesAsList();

            Assert.AreEqual(2, result.Count);
            CollectionAssert.Contains(result, "Company_ID");
            CollectionAssert.Contains(result, "Companies");
        }

        [TestMethod]
        public void GetForeignKeyPropertiesAsList_NullProperties_ReturnsEmptyList()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = null
            };

            // GetForeignKeyProperties throws on null; GetForeignKeyPropertiesAsList propagates
            Assert.ThrowsExactly<InvalidOperationException>(() => constraint.GetForeignKeyPropertiesAsList());
        }
    }
}
