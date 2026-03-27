using Apilane.Common.Extensions;

namespace Apilane.UnitTests
{
    [TestClass]
    public class TimespanExtensionsTests
    {
        [TestMethod]
        public void GetTimeRemainingString_LessThanOneSecond_ReturnsASecond()
        {
            var timeSpan = TimeSpan.FromMilliseconds(500);

            var result = timeSpan.GetTimeRemainingString();

            Assert.AreEqual("a second", result);
        }

        [TestMethod]
        public void GetTimeRemainingString_ExactlyZero_ReturnsASecond()
        {
            var timeSpan = TimeSpan.Zero;

            var result = timeSpan.GetTimeRemainingString();

            Assert.AreEqual("a second", result);
        }

        [TestMethod]
        public void GetTimeRemainingString_Seconds_ReturnsSecondsString()
        {
            var timeSpan = TimeSpan.FromSeconds(45);

            var result = timeSpan.GetTimeRemainingString();

            Assert.AreEqual("45 seconds", result);
        }

        [TestMethod]
        public void GetTimeRemainingString_OneSecond_ReturnsSecondsString()
        {
            var timeSpan = TimeSpan.FromSeconds(1);

            var result = timeSpan.GetTimeRemainingString();

            Assert.AreEqual("1 seconds", result);
        }

        [TestMethod]
        public void GetTimeRemainingString_Minutes_ReturnsMinutesAndSecondsString()
        {
            var timeSpan = new TimeSpan(0, 3, 20); // 3 min 20 sec

            var result = timeSpan.GetTimeRemainingString();

            Assert.AreEqual("3 minutes and 20 seconds", result);
        }

        [TestMethod]
        public void GetTimeRemainingString_ExactlyOneMinute_ReturnsMinutesAndSecondsString()
        {
            var timeSpan = TimeSpan.FromMinutes(1);

            var result = timeSpan.GetTimeRemainingString();

            Assert.AreEqual("1 minutes and 0 seconds", result);
        }

        [TestMethod]
        public void GetTimeRemainingString_Hours_ReturnsHoursMinutesSecondsString()
        {
            var timeSpan = new TimeSpan(2, 15, 30); // 2 hrs 15 min 30 sec

            var result = timeSpan.GetTimeRemainingString();

            Assert.AreEqual("2 hours, 15 minutes, and 30 seconds", result);
        }

        [TestMethod]
        public void GetTimeRemainingString_ExactlyOneHour_ReturnsHoursMinutesSecondsString()
        {
            var timeSpan = TimeSpan.FromHours(1);

            var result = timeSpan.GetTimeRemainingString();

            Assert.AreEqual("1 hours, 0 minutes, and 0 seconds", result);
        }

        [TestMethod]
        public void GetTimeRemainingString_MultipleHours_ReturnsCorrectString()
        {
            var timeSpan = new TimeSpan(10, 0, 0); // 10 hours exactly

            var result = timeSpan.GetTimeRemainingString();

            Assert.AreEqual("10 hours, 0 minutes, and 0 seconds", result);
        }
    }
}
