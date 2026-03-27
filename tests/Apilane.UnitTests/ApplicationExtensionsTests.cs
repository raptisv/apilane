using Apilane.Common.Extensions;

namespace Apilane.UnitTests
{
    [TestClass]
    public class ApplicationExtensionsTests
    {
        [TestMethod]
        public void GetDifferentiationPropertyName_SimpleEntity_ReturnsEntityUnderscoreID()
        {
            var result = "Company".GetDifferentiationPropertyName();

            Assert.AreEqual("Company_ID", result);
        }

        [TestMethod]
        public void GetDifferentiationPropertyName_MultiWordEntity_AppendsUnderscoreID()
        {
            var result = "OrganizationUnit".GetDifferentiationPropertyName();

            Assert.AreEqual("OrganizationUnit_ID", result);
        }

        [TestMethod]
        public void GetDifferentiationPropertyName_AlwaysEndsWithUnderscoredID()
        {
            var entities = new[] { "Company", "Tenant", "Account", "Group" };

            foreach (var entity in entities)
            {
                var result = entity.GetDifferentiationPropertyName();
                Assert.IsTrue(result.EndsWith("_ID"), $"Expected '{entity}_ID' but got '{result}'");
                Assert.AreEqual($"{entity}_ID", result);
            }
        }

        [TestMethod]
        public void GetDifferentiationPropertyName_IsDeterministic()
        {
            var entity = "Department";

            var r1 = entity.GetDifferentiationPropertyName();
            var r2 = entity.GetDifferentiationPropertyName();

            Assert.AreEqual(r1, r2);
        }
    }
}
