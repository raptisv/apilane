using System;

namespace Apilane.Common.Extensions
{
    public static class TimespanExtensions
    {
        public static string GetTimeRemainingString(this TimeSpan timeSpan)
        {
            // Check if the TimeSpan is less than a second
            if (timeSpan.TotalSeconds < 1)
            {
                return "a second";
            }

            // If the TimeSpan is in hours or more
            if (timeSpan.TotalHours >= 1)
            {
                return $"{(int)timeSpan.TotalHours} hours, {timeSpan.Minutes} minutes, and {timeSpan.Seconds} seconds";
            }

            // If the TimeSpan is in minutes but less than an hour
            if (timeSpan.TotalMinutes >= 1)
            {
                return $"{(int)timeSpan.TotalMinutes} minutes and {timeSpan.Seconds} seconds";
            }

            // If the TimeSpan is in seconds but less than a minute
            return $"{(int)timeSpan.TotalSeconds} seconds";
        }
    }
}
