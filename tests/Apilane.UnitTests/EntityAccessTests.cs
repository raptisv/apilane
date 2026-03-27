using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Api.Core.Services;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class EntityAccessTests
    {
        private static List<DBWS_EntityProperty> MakeProps(params string[] names)
        {
            return names.Select(n => new DBWS_EntityProperty
            {
                ID = 1,
                EntityID = 1,
                Name = n,
                TypeID = (int)PropertyType.String,
                IsPrimaryKey = false,
                IsSystem = false,
                Required = false,
                DateModified = DateTime.UtcNow
            }).ToList();
        }

        private static Users MakeUser(string? roles = null, long? diffValue = null)
        {
            return new Users
            {
                ID = 1,
                Email = "test@test.com",
                Username = "testuser",
                EmailConfirmed = true,
                Password = "hashedpassword",
                Roles = roles,
                Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LastLogin = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                DifferentiationPropertyValue = diffValue
            };
        }

        private static DBWS_Security MakeSecurity(
            string name, string roleId, string action,
            SecurityTypes type = SecurityTypes.Entity,
            int record = 0,
            string? properties = null)
        {
            return new DBWS_Security
            {
                Name = name,
                TypeID = (int)type,
                RoleID = roleId,
                Action = action,
                Record = record,
                Properties = properties
            };
        }

        // --- GetFull() ---

        [TestMethod]
        public void GetFull_ReturnsOneSecurityEntry()
        {
            var props = MakeProps("Name", "Price");
            var result = EntityAccess.GetFull("Products", props, SecurityActionType.get);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void GetFull_RecordIsAll()
        {
            var props = MakeProps("Name");
            var result = EntityAccess.GetFull("Products", props, SecurityActionType.get);
            Assert.AreEqual((int)EndpointRecordAuthorization.All, result[0].Record);
        }

        [TestMethod]
        public void GetFull_PropertiesJoinsAllPropNames()
        {
            var props = MakeProps("Name", "Price", "Stock");
            var result = EntityAccess.GetFull("Products", props, SecurityActionType.get);
            var secProps = result[0].GetProperties();
            CollectionAssert.Contains(secProps, "Name");
            CollectionAssert.Contains(secProps, "Price");
            CollectionAssert.Contains(secProps, "Stock");
        }

        [TestMethod]
        public void GetFull_RateLimitIsNull()
        {
            var result = EntityAccess.GetFull("Products", MakeProps("Name"), SecurityActionType.get);
            Assert.IsNull(result[0].RateLimit);
        }

        // --- GetMaximum() ---

        [TestMethod]
        public void GetMaximum_NoMatchingSecurity_ReturnsEmpty()
        {
            var secList = new List<DBWS_Security>
            {
                MakeSecurity("Orders", Globals.ANONYMOUS, "get")
            };
            var result = EntityAccess.GetMaximum(null, secList, "Products", SecurityTypes.Entity, SecurityActionType.get);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetMaximum_AnonymousSecurityExistsNullUser_ReturnsAnonymousEntry()
        {
            var secList = new List<DBWS_Security>
            {
                MakeSecurity("Products", Globals.ANONYMOUS, "get")
            };
            var result = EntityAccess.GetMaximum(null, secList, "Products", SecurityTypes.Entity, SecurityActionType.get);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(Globals.ANONYMOUS, result[0].RoleID);
        }

        [TestMethod]
        public void GetMaximum_AuthenticatedSecurityNullUser_ReturnsEmpty()
        {
            var secList = new List<DBWS_Security>
            {
                MakeSecurity("Products", Globals.AUTHENTICATED, "get")
            };
            // No user → AUTHENTICATED security should NOT be returned
            var result = EntityAccess.GetMaximum(null, secList, "Products", SecurityTypes.Entity, SecurityActionType.get);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetMaximum_AuthenticatedSecurityWithUser_ReturnsEntry()
        {
            var user = MakeUser();
            var secList = new List<DBWS_Security>
            {
                MakeSecurity("Products", Globals.AUTHENTICATED, "get")
            };
            var result = EntityAccess.GetMaximum(user, secList, "Products", SecurityTypes.Entity, SecurityActionType.get);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(Globals.AUTHENTICATED, result[0].RoleID);
        }

        [TestMethod]
        public void GetMaximum_RoleSecurityMatchesUserRole_ReturnsEntry()
        {
            var user = MakeUser(roles: "admin");
            var secList = new List<DBWS_Security>
            {
                MakeSecurity("Products", "admin", "get")
            };
            var result = EntityAccess.GetMaximum(user, secList, "Products", SecurityTypes.Entity, SecurityActionType.get);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("admin", result[0].RoleID);
        }

        [TestMethod]
        public void GetMaximum_RoleSecurityNoMatchingRole_ReturnsEmpty()
        {
            var user = MakeUser(roles: "viewer");
            var secList = new List<DBWS_Security>
            {
                MakeSecurity("Products", "admin", "get")
            };
            var result = EntityAccess.GetMaximum(user, secList, "Products", SecurityTypes.Entity, SecurityActionType.get);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetMaximum_BothAnonymousAndAuthenticatedWithUser_ReturnsBoth()
        {
            var user = MakeUser();
            var secList = new List<DBWS_Security>
            {
                MakeSecurity("Products", Globals.ANONYMOUS, "get"),
                MakeSecurity("Products", Globals.AUTHENTICATED, "get")
            };
            var result = EntityAccess.GetMaximum(user, secList, "Products", SecurityTypes.Entity, SecurityActionType.get);
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void GetMaximum_ActionMismatch_ReturnsEmpty()
        {
            var secList = new List<DBWS_Security>
            {
                MakeSecurity("Products", Globals.ANONYMOUS, "post")
            };
            var result = EntityAccess.GetMaximum(null, secList, "Products", SecurityTypes.Entity, SecurityActionType.get);
            Assert.AreEqual(0, result.Count);
        }
    }
}
