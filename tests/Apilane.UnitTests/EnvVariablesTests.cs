using Apilane.Common.Enums;
using Apilane.Common.Utilities;
using System;

namespace Apilane.UnitTests
{
    [TestClass]
    public class EnvVariablesTests
    {
        private const string TestVarName = "APILANE_TEST_ENV_VAR_UNIT_TESTS";

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable(TestVarName, null);
        }

        // ─── GetEnvironment ──────────────────────────────────────────────────────

        [TestMethod]
        public void GetEnvironment_VariableNotSet_ReturnsDefault()
        {
            Environment.SetEnvironmentVariable(TestVarName, null);
            var result = EnvVariables.GetEnvironment(TestVarName, HostingEnvironment.Development);
            Assert.AreEqual(HostingEnvironment.Development, result);
        }

        [TestMethod]
        public void GetEnvironment_VariableSetToProduction_ReturnsProduction()
        {
            Environment.SetEnvironmentVariable(TestVarName, "Production");
            var result = EnvVariables.GetEnvironment(TestVarName);
            Assert.AreEqual(HostingEnvironment.Production, result);
        }

        [TestMethod]
        public void GetEnvironment_VariableSetToDevelopment_ReturnsDevelopment()
        {
            Environment.SetEnvironmentVariable(TestVarName, "Development");
            var result = EnvVariables.GetEnvironment(TestVarName);
            Assert.AreEqual(HostingEnvironment.Development, result);
        }

        [TestMethod]
        public void GetEnvironment_InvalidValue_ReturnsDefault()
        {
            Environment.SetEnvironmentVariable(TestVarName, "InvalidEnvName");
            var result = EnvVariables.GetEnvironment(TestVarName, HostingEnvironment.Production);
            Assert.AreEqual(HostingEnvironment.Production, result);
        }

        [TestMethod]
        public void GetEnvironment_DefaultIsDevWhenNotSpecified()
        {
            Environment.SetEnvironmentVariable(TestVarName, null);
            var result = EnvVariables.GetEnvironment(TestVarName);
            Assert.AreEqual(HostingEnvironment.Development, result);
        }
    }
}
