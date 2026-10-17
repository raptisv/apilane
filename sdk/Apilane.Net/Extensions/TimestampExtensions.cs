using System;

namespace Apilane.Net.Extensions
{
    public static class TimestampExtensions
    {
        /// <summary>
        /// Returns the unix timestapm in seconds
        /// </summary>
        public static long ToUnixTimestampSeconds(this DateTime date)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan diff = date - origin;
            return (long)Math.Floor(diff.TotalSeconds);
        }

        /// <summary>
        /// Returns the unix timestapm in milliseconds
        /// </summary>
        public static long ToUnixTimestampMilliseconds(this DateTime date)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan diff = date - origin;
            return (long)diff.TotalMilliseconds;
        }

        /// <summary>
        /// Returns the DateTime of a unix timestamp, read the way the API reads it: a positive number of exactly
        /// 10 digits is seconds, every other value (a negative one too, a date before 1970) is milliseconds.
        /// Null when the value is outside the dates a DateTime can hold.
        /// </summary>
        public static DateTime? UnixTimestampToDatetime(this long value)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            if (value >= 1_000_000_000L && value <= 9_999_999_999L)
            {
                return epoch.AddSeconds(value);
            }

            // In ticks: a double would round the largest value up, past the end of DateTime
            var minMilliseconds = (DateTime.MinValue.Ticks - epoch.Ticks) / TimeSpan.TicksPerMillisecond;
            var maxMilliseconds = (DateTime.MaxValue.Ticks - epoch.Ticks) / TimeSpan.TicksPerMillisecond;

            if (value < minMilliseconds || value > maxMilliseconds)
            {
                return null;
            }

            return epoch.AddTicks(value * TimeSpan.TicksPerMillisecond);
        }
    }
}
