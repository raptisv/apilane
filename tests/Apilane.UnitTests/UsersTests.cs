using Apilane.Api.Core.Models.AppModules.Authentication;

namespace Apilane.UnitTests
{
    [TestClass]
    public class UsersTests
    {
        private static Users BuildUser(string? roles) => new Users
        {
            ID = 1,
            Email = "test@example.com",
            Username = "testuser",
            EmailConfirmed = true,
            Password = "hashed",
            Roles = roles,
            Created = 1000000,
            LastLogin = 2000000,
            DifferentiationPropertyValue = null
        };

        // ─── GetRoles ────────────────────────────────────────────────────────────

        [TestMethod]
        public void GetRoles_NullRoles_ReturnsEmptyList()
        {
            var user = BuildUser(null);
            var roles = user.GetRoles();
            Assert.IsNotNull(roles);
            Assert.AreEqual(0, roles.Count);
        }

        [TestMethod]
        public void GetRoles_EmptyRoles_ReturnsEmptyList()
        {
            var user = BuildUser(string.Empty);
            var roles = user.GetRoles();
            Assert.AreEqual(0, roles.Count);
        }

        [TestMethod]
        public void GetRoles_SingleRole_ReturnsSingleItem()
        {
            var user = BuildUser("Admin");
            var roles = user.GetRoles();
            Assert.AreEqual(1, roles.Count);
            Assert.AreEqual("Admin", roles[0]);
        }

        [TestMethod]
        public void GetRoles_MultipleRoles_ReturnsAllItems()
        {
            var user = BuildUser("Admin,Editor,Viewer");
            var roles = user.GetRoles();
            Assert.AreEqual(3, roles.Count);
            CollectionAssert.Contains(roles, "Admin");
            CollectionAssert.Contains(roles, "Editor");
            CollectionAssert.Contains(roles, "Viewer");
        }

        [TestMethod]
        public void GetRoles_WhitespaceOnlyEntries_AreFiltered()
        {
            var user = BuildUser("Admin,,Viewer");
            var roles = user.GetRoles();
            // Empty entries from double comma are filtered out
            Assert.AreEqual(2, roles.Count);
            CollectionAssert.Contains(roles, "Admin");
            CollectionAssert.Contains(roles, "Viewer");
        }

        [TestMethod]
        public void GetRoles_PreservesRoleValues()
        {
            var user = BuildUser("SuperAdmin");
            var roles = user.GetRoles();
            Assert.AreEqual("SuperAdmin", roles[0]);
        }
    }
}
