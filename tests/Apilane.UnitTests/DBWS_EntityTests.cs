using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using System.Text.Json;

namespace Apilane.UnitTests
{
    [TestClass]
    public class DBWS_EntityTests
    {
        private static DBWS_EntityProperty MakeProp(string name, bool isSystem = false, PropertyType type = PropertyType.String)
        {
            return new DBWS_EntityProperty
            {
                ID = 1,
                EntityID = 1,
                Name = name,
                TypeID = (int)type,
                IsPrimaryKey = false,
                IsSystem = isSystem,
                Required = false,
                DateModified = DateTime.UtcNow
            };
        }

        private static DBWS_Entity MakeEntity(
            string name = "MyEntity",
            bool isReadOnly = false,
            bool isSystem = false,
            List<DBWS_EntityProperty>? properties = null,
            string? entConstraints = null,
            string? entDefaultOrder = null)
        {
            return new DBWS_Entity
            {
                ID = 1,
                AppID = 1,
                Name = name,
                IsReadOnly = isReadOnly,
                IsSystem = isSystem,
                HasDifferentiationProperty = false,
                RequireChangeTracking = false,
                Properties = properties ?? new List<DBWS_EntityProperty>(),
                EntConstraints = entConstraints,
                EntDefaultOrder = entDefaultOrder,
                DateModified = DateTime.UtcNow
            };
        }

        // --- AllowAddProperties() ---

        [TestMethod]
        public void AllowAddProperties_NormalEntity_ReturnsTrue()
        {
            var entity = MakeEntity(name: "Products");
            Assert.IsTrue(entity.AllowAddProperties());
        }

        [TestMethod]
        public void AllowAddProperties_ReadOnly_ReturnsFalse()
        {
            var entity = MakeEntity(name: "Products", isReadOnly: true);
            Assert.IsFalse(entity.AllowAddProperties());
        }

        [TestMethod]
        public void AllowAddProperties_FilesEntity_ReturnsFalse()
        {
            var entity = MakeEntity(name: "Files");
            Assert.IsFalse(entity.AllowAddProperties());
        }

        // --- AllowPost() ---

        [TestMethod]
        public void AllowPost_NormalEntity_ReturnsTrue()
        {
            var entity = MakeEntity(name: "Products");
            Assert.IsTrue(entity.AllowPost());
        }

        [TestMethod]
        public void AllowPost_ReadOnly_ReturnsFalse()
        {
            var entity = MakeEntity(name: "Products", isReadOnly: true);
            Assert.IsFalse(entity.AllowPost());
        }

        [TestMethod]
        public void AllowPost_UsersEntity_ReturnsFalse()
        {
            var entity = MakeEntity(name: "Users");
            Assert.IsFalse(entity.AllowPost());
        }

        // --- AllowPut() ---

        [TestMethod]
        public void AllowPut_NormalEntity_ReturnsTrue()
        {
            var entity = MakeEntity(name: "Products");
            Assert.IsTrue(entity.AllowPut());
        }

        [TestMethod]
        public void AllowPut_ReadOnly_ReturnsFalse()
        {
            var entity = MakeEntity(name: "Products", isReadOnly: true);
            Assert.IsFalse(entity.AllowPut());
        }

        [TestMethod]
        public void AllowPut_FilesEntity_ReturnsFalse()
        {
            var entity = MakeEntity(name: "Files");
            Assert.IsFalse(entity.AllowPut());
        }

        // --- AllowDelete() ---

        [TestMethod]
        public void AllowDelete_NormalEntity_ReturnsTrue()
        {
            var entity = MakeEntity(name: "Products");
            Assert.IsTrue(entity.AllowDelete());
        }

        [TestMethod]
        public void AllowDelete_ReadOnly_ReturnsFalse()
        {
            var entity = MakeEntity(name: "Products", isReadOnly: true);
            Assert.IsFalse(entity.AllowDelete());
        }

        // --- HasOwnerColumn() ---

        [TestMethod]
        public void HasOwnerColumn_WithSystemOwnerProp_ReturnsTrue()
        {
            var props = new List<DBWS_EntityProperty>
            {
                MakeProp(Globals.OwnerColumn, isSystem: true)
            };
            var entity = MakeEntity(properties: props);
            Assert.IsTrue(entity.HasOwnerColumn());
        }

        [TestMethod]
        public void HasOwnerColumn_NonSystemOwnerProp_ReturnsFalse()
        {
            // IsSystem must be true for HasOwnerColumn to return true
            var props = new List<DBWS_EntityProperty>
            {
                MakeProp(Globals.OwnerColumn, isSystem: false)
            };
            var entity = MakeEntity(properties: props);
            Assert.IsFalse(entity.HasOwnerColumn());
        }

        [TestMethod]
        public void HasOwnerColumn_NoOwnerProp_ReturnsFalse()
        {
            var props = new List<DBWS_EntityProperty> { MakeProp("SomeOtherProp") };
            var entity = MakeEntity(properties: props);
            Assert.IsFalse(entity.HasOwnerColumn());
        }

        // --- Constraints (JSON round-trip) ---

        [TestMethod]
        public void Constraints_ValidJson_DeserializesCorrectly()
        {
            var constraints = new List<EntityConstraint>
            {
                new() { IsSystem = true, TypeID = (int)ConstraintType.Unique, Properties = "Email" }
            };
            var json = JsonSerializer.Serialize(constraints);
            var entity = MakeEntity(entConstraints: json);

            Assert.AreEqual(1, entity.Constraints.Count);
            Assert.AreEqual((int)ConstraintType.Unique, entity.Constraints[0].TypeID);
            Assert.AreEqual("Email", entity.Constraints[0].Properties);
        }

        [TestMethod]
        public void Constraints_NullEntConstraints_ReturnsEmptyList()
        {
            var entity = MakeEntity(entConstraints: null);
            Assert.AreEqual(0, entity.Constraints.Count);
        }

        [TestMethod]
        public void Constraints_InvalidJson_ReturnsEmptyList()
        {
            var entity = MakeEntity(entConstraints: "not valid json at all");
            Assert.AreEqual(0, entity.Constraints.Count);
        }

        // --- DefaultOrder (via SortData.ParseList) ---

        [TestMethod]
        public void DefaultOrder_NullEntDefaultOrder_ReturnsEmpty()
        {
            var entity = MakeEntity(entDefaultOrder: null);
            Assert.IsFalse(entity.DefaultOrder.Any());
        }

        [TestMethod]
        public void DefaultOrder_ValidJson_ReturnsParsedSortData()
        {
            var json = "[{\"Property\":\"Name\",\"Direction\":\"Asc\"}]";
            var entity = MakeEntity(entDefaultOrder: json);
            var orders = entity.DefaultOrder.ToList();

            Assert.AreEqual(1, orders.Count);
            Assert.AreEqual("Name", orders[0].Property);
            Assert.AreEqual("Asc", orders[0].Direction);
        }
    }
}
