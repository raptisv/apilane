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

            Assert.ThrowsException<InvalidOperationException>(() => constraint.GetUniqueProperties());
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

            Assert.ThrowsException<InvalidOperationException>(() => constraint.GetForeignKeyProperties());
        }

        [TestMethod]
        public void GetForeignKeyProperties_OneElement_Throws()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = "OnlyOne"
            };

            Assert.ThrowsException<InvalidOperationException>(() => constraint.GetForeignKeyProperties());
        }

        [TestMethod]
        public void GetForeignKeyProperties_WrongConstraintType_Throws()
        {
            var constraint = new EntityConstraint
            {
                TypeID = (int)ConstraintType.Unique,
                Properties = "Col,Table"
            };

            Assert.ThrowsException<InvalidOperationException>(() => constraint.GetForeignKeyProperties());
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
            Assert.ThrowsException<InvalidOperationException>(() => constraint.GetForeignKeyPropertiesAsList());
        }
    }
}
