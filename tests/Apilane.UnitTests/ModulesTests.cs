using Apilane.Api.Core.AppModules;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Api.Core.Models.AppModules.Files;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Models.AppModules.Authentication;

namespace Apilane.UnitTests
{
    [TestClass]
    public class ModulesTests
    {
        // --- NewApplicationSystemEntities ---

        [TestMethod]
        public void NewApplicationSystemEntities_NoDiffEntity_ReturnsThreeSystemEntities()
        {
            var entities = Modules.NewApplicationSystemEntities(string.Empty);
            // Users, AuthTokens, Files
            Assert.AreEqual(3, entities.Count);
        }

        [TestMethod]
        public void NewApplicationSystemEntities_WithDiffEntity_ReturnsFourEntities()
        {
            var entities = Modules.NewApplicationSystemEntities("Tenant");
            // Tenant + Users + AuthTokens + Files
            Assert.AreEqual(4, entities.Count);
        }

        [TestMethod]
        public void NewApplicationSystemEntities_AllEntitiesAreSystem()
        {
            var entities = Modules.NewApplicationSystemEntities("Tenant");
            Assert.IsTrue(entities.All(e => e.IsSystem));
        }

        [TestMethod]
        public void NewApplicationSystemEntities_ContainsUsersAuthTokensFiles()
        {
            var entities = Modules.NewApplicationSystemEntities(string.Empty);
            var names = entities.Select(e => e.Name).ToList();
            CollectionAssert.Contains(names, nameof(Users));
            CollectionAssert.Contains(names, nameof(AuthTokens));
            CollectionAssert.Contains(names, nameof(Files));
        }

        [TestMethod]
        public void NewApplicationSystemEntities_DiffEntityIsFirst()
        {
            var entities = Modules.NewApplicationSystemEntities("Tenant");
            Assert.AreEqual("Tenant", entities[0].Name);
        }

        [TestMethod]
        public void NewApplicationSystemEntities_WithDiffEntity_UsersHasDiffProperty()
        {
            var entities = Modules.NewApplicationSystemEntities("Tenant");
            var users = entities.First(e => e.Name == nameof(Users));
            // Users has HasDifferentiationProperty=true, so it should get the Tenant_ID property
            var diffPropName = "Tenant_ID";
            Assert.IsTrue(users.Properties.Any(p => p.Name == diffPropName),
                $"Expected property '{diffPropName}' in Users entity");
        }

        [TestMethod]
        public void NewApplicationSystemEntities_WithDiffEntity_UsersConstraintsContainFKToDiffEntity()
        {
            var entities = Modules.NewApplicationSystemEntities("Tenant");
            var users = entities.First(e => e.Name == nameof(Users));
            var constraints = users.Constraints;
            Assert.IsTrue(constraints.Any(c => c.TypeID == (int)ConstraintType.ForeignKey &&
                c.Properties != null && c.Properties.Contains("Tenant")),
                "Expected a FK constraint referencing Tenant in Users");
        }

        [TestMethod]
        public void NewApplicationSystemEntities_AuthTokensIsReadOnly()
        {
            var entities = Modules.NewApplicationSystemEntities(string.Empty);
            var authTokens = entities.First(e => e.Name == nameof(AuthTokens));
            Assert.IsTrue(authTokens.IsReadOnly);
        }

        // --- NewEntityPropertiesConstraints ---

        [TestMethod]
        public void NewEntityPropertiesConstraints_NoDiffEntity_ReturnsThreeProperties()
        {
            var (props, constraints) = Modules.NewEntityPropertiesConstraints(null, false);
            // ID, Owner, Created
            Assert.AreEqual(3, props.Count);
        }

        [TestMethod]
        public void NewEntityPropertiesConstraints_NoDiffEntity_ReturnsOneConstraint()
        {
            var (_, constraints) = Modules.NewEntityPropertiesConstraints(null, false);
            // FK: Owner -> Users
            Assert.AreEqual(1, constraints.Count);
        }

        [TestMethod]
        public void NewEntityPropertiesConstraints_WithDiffEntityAndFlag_ReturnsFourProperties()
        {
            var (props, _) = Modules.NewEntityPropertiesConstraints("Tenant", true);
            // ID, Owner, Created, Tenant_ID
            Assert.AreEqual(4, props.Count);
        }

        [TestMethod]
        public void NewEntityPropertiesConstraints_WithDiffEntityAndFlag_ReturnsTwoConstraints()
        {
            var (_, constraints) = Modules.NewEntityPropertiesConstraints("Tenant", true);
            // FK: Owner->Users + FK: Tenant_ID->Tenant
            Assert.AreEqual(2, constraints.Count);
        }

        [TestMethod]
        public void NewEntityPropertiesConstraints_DiffEntityFlagFalse_ReturnsThreePropertiesAndOneConstraint()
        {
            // differentiationEntity set but entityHasDifferentiationProperty = false
            var (props, constraints) = Modules.NewEntityPropertiesConstraints("Tenant", false);
            Assert.AreEqual(3, props.Count);
            Assert.AreEqual(1, constraints.Count);
        }

        [TestMethod]
        public void NewEntityPropertiesConstraints_AlwaysHasPrimaryKeyProperty()
        {
            var (props, _) = Modules.NewEntityPropertiesConstraints(null, false);
            Assert.IsTrue(props.Any(p => p.IsPrimaryKey && p.Name == Globals.PrimaryKeyColumn));
        }

        [TestMethod]
        public void NewEntityPropertiesConstraints_AlwaysHasCreatedProperty()
        {
            var (props, _) = Modules.NewEntityPropertiesConstraints(null, false);
            Assert.IsTrue(props.Any(p => p.Name == Globals.CreatedColumn));
        }

        [TestMethod]
        public void NewEntityPropertiesConstraints_AlwaysHasOwnerProperty()
        {
            var (props, _) = Modules.NewEntityPropertiesConstraints(null, false);
            Assert.IsTrue(props.Any(p => p.Name == Globals.OwnerColumn));
        }
    }
}
