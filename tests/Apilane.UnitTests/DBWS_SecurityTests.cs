using Apilane.Common.Enums;
using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class DBWS_SecurityTests
    {
        // --- RateLimitItem.TimeWindow ---

        [TestMethod]
        public void RateLimitItem_TimeWindow_PerSecond_ReturnsOneSecond()
        {
            var item = DBWS_Security.RateLimitItem.New(10, EndpointRateLimit.Per_Second);
            Assert.AreEqual(TimeSpan.FromSeconds(1), item.TimeWindow);
        }

        [TestMethod]
        public void RateLimitItem_TimeWindow_PerMinute_ReturnsOneMinute()
        {
            var item = DBWS_Security.RateLimitItem.New(10, EndpointRateLimit.Per_Minute);
            Assert.AreEqual(TimeSpan.FromMinutes(1), item.TimeWindow);
        }

        [TestMethod]
        public void RateLimitItem_TimeWindow_PerHour_ReturnsOneHour()
        {
            var item = DBWS_Security.RateLimitItem.New(10, EndpointRateLimit.Per_Hour);
            Assert.AreEqual(TimeSpan.FromHours(1), item.TimeWindow);
        }

        [TestMethod]
        public void RateLimitItem_TimeWindow_None_ReturnsZero()
        {
            var item = DBWS_Security.RateLimitItem.New(10, EndpointRateLimit.None);
            Assert.AreEqual(TimeSpan.Zero, item.TimeWindow);
        }

        // --- RateLimitItem.RequestPerSecond() ---

        [TestMethod]
        public void RateLimitItem_RequestPerSecond_PerSecond_EqualsMaxRequests()
        {
            var item = DBWS_Security.RateLimitItem.New(5, EndpointRateLimit.Per_Second);
            Assert.AreEqual(5.0, item.RequestPerSecond(), 1e-9);
        }

        [TestMethod]
        public void RateLimitItem_RequestPerSecond_PerMinute_DividesBy60()
        {
            var item = DBWS_Security.RateLimitItem.New(120, EndpointRateLimit.Per_Minute);
            Assert.AreEqual(2.0, item.RequestPerSecond(), 1e-9);
        }

        [TestMethod]
        public void RateLimitItem_RequestPerSecond_PerHour_DividesBy3600()
        {
            var item = DBWS_Security.RateLimitItem.New(3600, EndpointRateLimit.Per_Hour);
            Assert.AreEqual(1.0, item.RequestPerSecond(), 1e-9);
        }

        // --- RateLimitItem.ToUniqueString() ---

        [TestMethod]
        public void RateLimitItem_ToUniqueString_IsLowercase()
        {
            var item = DBWS_Security.RateLimitItem.New(10, EndpointRateLimit.Per_Second);
            var result = item.ToUniqueString();
            Assert.AreEqual(result, result.ToLower());
        }

        [TestMethod]
        public void RateLimitItem_ToUniqueString_ContainsMaxRequests()
        {
            var item = DBWS_Security.RateLimitItem.New(42, EndpointRateLimit.Per_Minute);
            Assert.IsTrue(item.ToUniqueString().Contains("42"));
        }

        // --- RateLimitItem.New() factory ---

        [TestMethod]
        public void RateLimitItem_New_SetsMaxRequestsAndTimeWindowType()
        {
            var item = DBWS_Security.RateLimitItem.New(100, EndpointRateLimit.Per_Hour);
            Assert.AreEqual(100, item.MaxRequests);
            Assert.AreEqual((int)EndpointRateLimit.Per_Hour, item.TimeWindowType);
        }

        // --- DBWS_Security.GetProperties() ---

        [TestMethod]
        public void GetProperties_CommaSeparated_ReturnsList()
        {
            var sec = new DBWS_Security { Name = "E", TypeID = 0, RoleID = "r", Action = "get", Record = 0, Properties = "Name,Email,Age" };
            var props = sec.GetProperties();
            CollectionAssert.AreEqual(new List<string> { "Name", "Email", "Age" }, props);
        }

        [TestMethod]
        public void GetProperties_NullProperties_ReturnsEmptyList()
        {
            var sec = new DBWS_Security { Name = "E", TypeID = 0, RoleID = "r", Action = "get", Record = 0, Properties = null };
            Assert.AreEqual(0, sec.GetProperties().Count);
        }

        [TestMethod]
        public void GetProperties_EmptyProperties_ReturnsEmptyList()
        {
            var sec = new DBWS_Security { Name = "E", TypeID = 0, RoleID = "r", Action = "get", Record = 0, Properties = "" };
            Assert.AreEqual(0, sec.GetProperties().Count);
        }

        // --- ToUniqueStringShort() / ToUniqueStringLong() / NameDescriptive() ---

        [TestMethod]
        public void ToUniqueStringShort_ContainsAllParts()
        {
            var sec = new DBWS_Security { Name = "Products", TypeID = 0, RoleID = "admin", Action = "get", Record = 0 };
            var result = sec.ToUniqueStringShort();
            Assert.IsTrue(result.Contains("Products"));
            Assert.IsTrue(result.Contains("admin"));
            Assert.IsTrue(result.Contains("get"));
        }

        [TestMethod]
        public void ToUniqueStringLong_WithRateLimit_ContainsRateLimitString()
        {
            var sec = new DBWS_Security
            {
                Name = "Products",
                TypeID = 0,
                RoleID = "admin",
                Action = "get",
                Record = 0,
                Properties = "Name,Price",
                RateLimit = DBWS_Security.RateLimitItem.New(10, EndpointRateLimit.Per_Second)
            };
            var result = sec.ToUniqueStringLong();
            Assert.IsTrue(result.Contains("10"));
            // Properties should be sorted alphabetically
            Assert.IsTrue(result.Contains("Name"));
            Assert.IsTrue(result.Contains("Price"));
        }

        [TestMethod]
        public void ToUniqueStringLong_PropertiesSortedAlphabetically()
        {
            var secA = new DBWS_Security { Name = "E", TypeID = 0, RoleID = "r", Action = "get", Record = 0, Properties = "Zzz,Aaa" };
            var secB = new DBWS_Security { Name = "E", TypeID = 0, RoleID = "r", Action = "get", Record = 0, Properties = "Aaa,Zzz" };
            // Both should produce the same long string since properties are sorted
            Assert.AreEqual(secA.ToUniqueStringLong(), secB.ToUniqueStringLong());
        }

        [TestMethod]
        public void NameDescriptive_ContainsTypeNameAndRoleAndAction()
        {
            var sec = new DBWS_Security
            {
                Name = "Users",
                TypeID = (int)SecurityTypes.Entity,
                RoleID = "ANONYMOUS",
                Action = "get",
                Record = 0
            };
            var result = sec.NameDescriptive();
            Assert.IsTrue(result.Contains("Users"));
            Assert.IsTrue(result.Contains("ANONYMOUS"));
            Assert.IsTrue(result.Contains("get"));
        }
    }
}
