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
        /// Returns the DateTime object given a unix timestamp given in seconds (10 digits) or milliseconds (13 digits) else null
        /// </summary>
        public static DateTime? UnixTimestampToDatetime(this long value)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            switch (value.ToString().Length)
            {
                case 10:
                    return epoch.AddSeconds(value);
                case 13:
                    return epoch.AddMilliseconds(value);
                default:
                    return null;
            }
        }
    }
}
