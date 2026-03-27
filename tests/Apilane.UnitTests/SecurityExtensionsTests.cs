using Apilane.Common.Enums;
using Apilane.Common.Extensions;

namespace Apilane.UnitTests
{
    [TestClass]
    public class SecurityExtensionsTests
    {
        [TestMethod]
        public void BuildRateLimitingGrainKeyExt_WithAuthenticatedUser_ContainsAllParts()
        {
            var maxRequests = 10;
            var timeWindow = TimeSpan.FromSeconds(1);
            var userIdentifier = "user-123";
            var entity = "Products";
            var action = SecurityActionType.get;

            var key = SecurityExtensions.BuildRateLimitingGrainKeyExt(maxRequests, timeWindow, userIdentifier, entity, action);

            StringAssert.Contains(key, "10");
            StringAssert.Contains(key, "00:00:01");
            StringAssert.Contains(key, "user-123");
            StringAssert.Contains(key, "Products");
            StringAssert.Contains(key, "get");
        }

        [TestMethod]
        public void BuildRateLimitingGrainKeyExt_WithNullUser_ContainsNullRepresentation()
        {
            var key = SecurityExtensions.BuildRateLimitingGrainKeyExt(5, TimeSpan.FromMinutes(1), null, "Entity", SecurityActionType.post);

            // null user identifier should still produce a valid key
            Assert.IsNotNull(key);
            StringAssert.Contains(key, "Entity");
            StringAssert.Contains(key, "post");
        }

        [TestMethod]
        public void BuildRateLimitingGrainKeyExt_DifferentUsers_ProduceDifferentKeys()
        {
            var key1 = SecurityExtensions.BuildRateLimitingGrainKeyExt(10, TimeSpan.FromSeconds(1), "user-A", "Entity", SecurityActionType.get);
            var key2 = SecurityExtensions.BuildRateLimitingGrainKeyExt(10, TimeSpan.FromSeconds(1), "user-B", "Entity", SecurityActionType.get);

            Assert.AreNotEqual(key1, key2);
        }

        [TestMethod]
        public void BuildRateLimitingGrainKeyExt_DifferentActions_ProduceDifferentKeys()
        {
            var key1 = SecurityExtensions.BuildRateLimitingGrainKeyExt(10, TimeSpan.FromSeconds(1), "user-A", "Entity", SecurityActionType.get);
            var key2 = SecurityExtensions.BuildRateLimitingGrainKeyExt(10, TimeSpan.FromSeconds(1), "user-A", "Entity", SecurityActionType.post);

            Assert.AreNotEqual(key1, key2);
        }

        [TestMethod]
        public void BuildRateLimitingGrainKeyExt_DifferentEntities_ProduceDifferentKeys()
        {
            var key1 = SecurityExtensions.BuildRateLimitingGrainKeyExt(10, TimeSpan.FromSeconds(1), "user", "EntityA", SecurityActionType.get);
            var key2 = SecurityExtensions.BuildRateLimitingGrainKeyExt(10, TimeSpan.FromSeconds(1), "user", "EntityB", SecurityActionType.get);

            Assert.AreNotEqual(key1, key2);
        }

        [TestMethod]
        public void BuildRateLimitingGrainKeyExt_TimeWindowFormattedCorrectly()
        {
            // TimeSpan of 1 hour should format as "01:00:00"
            var key = SecurityExtensions.BuildRateLimitingGrainKeyExt(100, TimeSpan.FromHours(1), "user", "Entity", SecurityActionType.get);

            StringAssert.Contains(key, "01:00:00");
        }

        [TestMethod]
        public void BuildRateLimitingGrainKeyExt_IsDeterministic()
        {
            var key1 = SecurityExtensions.BuildRateLimitingGrainKeyExt(5, TimeSpan.FromMinutes(1), "user", "Entity", SecurityActionType.delete);
            var key2 = SecurityExtensions.BuildRateLimitingGrainKeyExt(5, TimeSpan.FromMinutes(1), "user", "Entity", SecurityActionType.delete);

            Assert.AreEqual(key1, key2);
        }
    }
}
