using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.AppModules;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Services;
using Apilane.Common;
using Apilane.Common.Abstractions;
using Apilane.Common.Models;
using Apilane.Common.Security;
using Apilane.Data.Abstractions;
using FakeItEasy;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace Apilane.UnitTests
{
    /// <summary>
    /// The password length rule must be identical on every path that accepts a password:
    /// the shared policy itself, the system Users entity definition (registration and Data/Put),
    /// and the value conversion that hashes what gets stored.
    /// </summary>
    [TestClass]
    public class PasswordPolicyTests
    {
        [TestMethod]
        public void Validate_BoundariesMatchTheConstants()
        {
            Assert.AreEqual(8, PasswordPolicy.MinimumLength);
            Assert.AreEqual(400, PasswordPolicy.MaximumLength);

            Assert.AreEqual("Required", PasswordPolicy.Validate(null));
            Assert.AreEqual("Required", PasswordPolicy.Validate(""));
            Assert.AreEqual("Minimum 8 characters", PasswordPolicy.Validate(new string('p', 7)));
            Assert.IsNull(PasswordPolicy.Validate(new string('p', 8)));
            Assert.IsNull(PasswordPolicy.Validate(new string('p', 21))); // above the old change-password cap
            Assert.IsNull(PasswordPolicy.Validate(new string('p', 400)));
            Assert.AreEqual("Maximum 400 characters", PasswordPolicy.Validate(new string('p', 401)));
        }

        [TestMethod]
        public void UsersEntityDefinition_UsesThePolicyLimits()
        {
            var users = Modules.NewApplicationSystemEntities(string.Empty)
                .Single(e => e.Name.Equals(Globals.UsersEntityName));

            var password = users.Properties.Single(p => p.Name.Equals(Globals.PasswordColumn));

            Assert.AreEqual(PasswordPolicy.MinimumLength, password.Minimum);
            Assert.AreEqual(PasswordPolicy.MaximumLength, password.Maximum);
        }

        [TestMethod]
        public void GetPropertyValue_TooShortOrTooLongPassword_Throws()
        {
            var (service, users, password) = CreateFixture();

            var tooShort = Assert.ThrowsExactly<ApilaneException>(() =>
                service.GetPropertyValue(null, "unused", users, password, Body(new string('p', 7)), null));
            Assert.AreEqual(AppErrors.VALIDATION, tooShort.Error);
            Assert.AreEqual(Globals.PasswordColumn, tooShort.Property);

            var tooLong = Assert.ThrowsExactly<ApilaneException>(() =>
                service.GetPropertyValue(null, "unused", users, password, Body(new string('p', 401)), null));
            Assert.AreEqual(AppErrors.VALIDATION, tooLong.Error);
            Assert.AreEqual(Globals.PasswordColumn, tooLong.Property);
        }

        [TestMethod]
        public void GetPropertyValue_PasswordWithinLimits_IsStoredAsHash()
        {
            var (service, users, password) = CreateFixture();

            foreach (var length in new[] { PasswordPolicy.MinimumLength, 21, PasswordPolicy.MaximumLength })
            {
                var plaintext = new string('p', length);

                var stored = service.GetPropertyValue(null, "unused", users, password, Body(plaintext), null) as string;

                Assert.IsNotNull(stored, $"length {length}");
                Assert.IsTrue(PasswordHasher.IsHash(stored), $"length {length}");
                Assert.IsTrue(PasswordHasher.Verify(plaintext, stored), $"length {length}");
            }
        }

        [TestMethod]
        public void GetPropertyValue_AlreadyHashedValue_IsKeptVerbatim()
        {
            var (service, users, password) = CreateFixture();
            var hash = PasswordHasher.Hash("some password");

            var stored = service.GetPropertyValue(null, "unused", users, password, Body(hash), null);

            Assert.AreEqual(hash, stored);
        }

        private static JsonObject Body(string password) => new() { [Globals.PasswordColumn] = password };

        private static (ApplicationDataService Service, DBWS_Entity Users, DBWS_EntityProperty Password) CreateFixture()
        {
            var service = new ApplicationDataService(
                A.Fake<ILogger<ApplicationDataService>>(),
                A.Fake<IApplicationDataStoreFactory>(),
                A.Fake<IApplicationService>(),
                A.Fake<IApplicationHelperService>(),
                A.Fake<IEntityHistoryAPI>(),
                A.Fake<ITransactionScopeService>());

            var users = Modules.NewApplicationSystemEntities(string.Empty)
                .Single(e => e.Name.Equals(Globals.UsersEntityName));

            var password = users.Properties.Single(p => p.Name.Equals(Globals.PasswordColumn));

            return (service, users, password);
        }
    }
}
