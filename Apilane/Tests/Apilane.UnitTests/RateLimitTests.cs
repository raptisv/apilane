using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class RateLimitTests
    {
        [TestMethod]
        public void IsRateLimited_Empty_Should_Work()
        {
            var listOfSecurities = new List<DBWS_Security.RateLimitItem?>();

            var isRateLimited = listOfSecurities.IsRateLimited(out _, out _);

            Assert.IsFalse(isRateLimited);
        }

        [TestMethod]
        public void IsRateLimited_None_Should_Work()
        {
            var listOfSecurities = new List<DBWS_Security.RateLimitItem?>()
            {
                DBWS_Security.RateLimitItem.New(1, EndpointRateLimit.None)
            };

            var isRateLimited = listOfSecurities.IsRateLimited(out _, out _);

            Assert.IsFalse(isRateLimited);
        }

        [TestMethod]
        public void IsRateLimited_Per_Second_Should_Work()
        {
            var listOfSecurities = new List<DBWS_Security.RateLimitItem?>()
            {
                DBWS_Security.RateLimitItem.New(1, EndpointRateLimit.Per_Second)
            };

            var isRateLimited = listOfSecurities.IsRateLimited(out int maxREquests, out TimeSpan timeWindow);

            Assert.IsTrue(isRateLimited);
            Assert.AreEqual(maxREquests, 1);
            Assert.AreEqual(timeWindow, TimeSpan.FromSeconds(1));
        }

        [TestMethod]
        public void IsRateLimited_Per_Minute_Should_Work()
        {
            var listOfSecurities = new List<DBWS_Security.RateLimitItem?>()
            {
                DBWS_Security.RateLimitItem.New(1, EndpointRateLimit.Per_Minute)
            };

            var isRateLimited = listOfSecurities.IsRateLimited(out int maxREquests, out TimeSpan timeWindow);

            Assert.IsTrue(isRateLimited);
            Assert.AreEqual(maxREquests, 1);
            Assert.AreEqual(timeWindow, TimeSpan.FromMinutes(1));
        }

        [TestMethod]
        public void IsRateLimited_Per_Hour_Should_Work()
        {
            var listOfSecurities = new List<DBWS_Security.RateLimitItem?>()
            {
                DBWS_Security.RateLimitItem.New(1, EndpointRateLimit.Per_Hour)
            };

            var isRateLimited = listOfSecurities.IsRateLimited(out int maxREquests, out TimeSpan timeWindow);

            Assert.IsTrue(isRateLimited);
            Assert.AreEqual(maxREquests, 1);
            Assert.AreEqual(timeWindow, TimeSpan.FromHours(1));
        }

        [TestMethod]
        public void IsRateLimited_MultipleSecurities_Should_Return_Largest()
        {
            var listOfSecurities = new List<DBWS_Security.RateLimitItem?>()
            {
                DBWS_Security.RateLimitItem.New(1, EndpointRateLimit.Per_Second),
                DBWS_Security.RateLimitItem.New(2, EndpointRateLimit.Per_Second), // <- This is the largest
                DBWS_Security.RateLimitItem.New(60, EndpointRateLimit.Per_Hour)
            };

            var isRateLimited = listOfSecurities.IsRateLimited(out int maxREquests, out TimeSpan timeWindow);

            Assert.IsTrue(isRateLimited);
            Assert.AreEqual(maxREquests, 2);
            Assert.AreEqual(timeWindow, TimeSpan.FromSeconds(1));
        }
    }
}
