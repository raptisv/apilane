using System;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Apilane.Common
{
    public static class Utils
    {
        public static string GetString(object? val)
        {
            return val is not null
                ? val.ToString()?.Trim() ?? string.Empty
                : string.Empty;
        }

        public static int GetInt(object? val, int defval = -1)
        {
            if (val is null)
            {
                return defval;
            }

            var strVal = GetString(val);

            if (string.IsNullOrWhiteSpace(strVal))
            {
                return defval;
            }

            return int.TryParse(strVal, out int response) ?
                response :
                defval;
        }

        /// <summary>
        /// The page size of a list endpoint: 1 to <see cref="Globals.MaxPageSize"/>. Anything else (0, a negative
        /// number, too big, or none) becomes the maximum. A page size of 0 or less is read by the data layer as
        /// "no limit" (the API relies on that to load every record of a delete), so an endpoint must never
        /// hand it on as it came in from the request.
        /// </summary>
        public static int GetPageSize(int? pageSize)
        {
            return pageSize is >= 1 and <= Globals.MaxPageSize
                ? pageSize.Value
                : Globals.MaxPageSize;
        }

        public static int? GetNullInt(object val, int? defval = null)
        {
            var strVal = GetString(val);

            if (string.IsNullOrWhiteSpace(strVal))
                return defval;

            return int.TryParse(strVal, out int response) ?
                response :
                defval;
        }

        public static Int64 GetLong(object? val, long defval = -1)
        {
            var strVal = GetString(val);

            if (string.IsNullOrWhiteSpace(strVal))
                return defval;

            return long.TryParse(strVal, out long response) ?
                response :
                defval;
        }

        public static long? GetNullLong(object? val, long? defval = null)
        {
            var strVal = GetString(val);

            if (string.IsNullOrWhiteSpace(strVal))
                return defval;

            return long.TryParse(strVal, out long response) ?
                response :
                defval;
        }

        public static DateTime? GetDate(object val)
        {
            if (string.IsNullOrWhiteSpace(GetString(val)))
                return null;

            try
            {
                return DateTime.Parse(GetString(val));
            }
            catch
            {
                return null;
            }
        }

        public static DateTime GetDate(object val, DateTime defaultValue)
        {
            try
            {
                return DateTime.Parse(GetString(val));
            }
            catch
            {
                return defaultValue;
            }
        }

        public static float GetFloat(object val, int defval = -1)
        {
            var strVal = GetString(val);

            if (string.IsNullOrWhiteSpace(strVal))
                return defval;

            return float.TryParse(strVal.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out float response) ?
                response :
                defval;
        }

        public static float? GetNullFloat(object val, float? defval = null)
        {
            var strVal = GetString(val);

            if (string.IsNullOrWhiteSpace(strVal))
                return defval;

            return float.TryParse(strVal.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out float response) ?
                response :
                defval;
        }

        public static double GetDouble(object? val, int defval = -1)
        {
            if (val is null)
            {
                return defval;
            }

            var strVal = GetString(val);

            if (string.IsNullOrWhiteSpace(strVal))
                return defval;

            return double.TryParse(strVal.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double response)
                ? response
                : defval;
        }

        public static double? GetNullDouble(object val, double? defval = null)
        {
            var strVal = GetString(val);

            if (string.IsNullOrWhiteSpace(strVal))
                return defval;

            return double.TryParse(strVal.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double response) ?
                response :
                defval;
        }

        public static decimal GetDecimal(object val, decimal defval = -1)
        {
            var strVal = GetString(val);

            if (string.IsNullOrWhiteSpace(strVal))
                return defval;

            return decimal.TryParse(strVal.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal response) ?
                response :
                defval;
        }

        public static decimal? GetNullDecimal(object val, decimal? defval = null)
        {
            var strVal = GetString(val);

            if (string.IsNullOrWhiteSpace(strVal))
                return defval;

            return decimal.TryParse(strVal.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal response) ?
                response :
                defval;
        }

        public static bool GetBool(object? val, bool defval = false)
        {
            if (val is null)
                return defval;

            var strVal = GetString(val).ToLower();

            if (string.IsNullOrWhiteSpace(strVal))
                return defval;

            bool result;

            if (strVal.Equals("1") || strVal.Equals("true") || strVal.Equals("on"))
                result = true;
            else if (strVal.Equals("0") || strVal.Equals("false") || strVal.Equals("off"))
                result = false;
            else
                result = bool.TryParse(strVal, out bool parseVal) ? parseVal : defval;

            return result;
        }

        public static bool? GetNullBool(object val, bool? defval = null)
        {
            var strVal = GetString(val).ToLower();

            if (string.IsNullOrWhiteSpace(strVal))
                return defval;

            bool? result;

            if (strVal.Equals("1") || strVal.Equals("true") || strVal.Equals("on"))
                result = true;
            else if (strVal.Equals("0") || strVal.Equals("false") || strVal.Equals("off"))
                result = false;
            else
                result = bool.TryParse(strVal, out bool parseVal) ? (bool?)parseVal : defval;

            return result;
        }

        public static long GetUnixTimestampMilliseconds(DateTime date)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan diff = date - origin;
            return (long)diff.TotalMilliseconds;
        }

        public static DateTime? GetDateFromUnixTimestamp(string unixTime)
        {
            unixTime = Utils.GetString(unixTime);

            if (unixTime.All(char.IsDigit))
            {
                long number = Utils.GetLong(unixTime, 0);

                if (number >= 0)
                {
                    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    switch (unixTime.Length)
                    {
                        case 10:
                            return epoch.AddSeconds(number);
                        case 13:
                            return epoch.AddMilliseconds(number);
                    }
                }
            }

            return null;
        }

        // ISO 8601 with a 'T': seconds and their fraction are optional, and so is the zone ('Z' or an offset such as +02:00)
        private static readonly string[] _isoDateTimeFormats =
        {
            "yyyy-MM-dd'T'HH:mmK",
            "yyyy-MM-dd'T'HH:mm:ssK",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
        };

        // The dates a DateTime can hold, as Unix milliseconds (in ticks, a double would round the maximum up)
        private static readonly long _minUnixTimestampMilliseconds = (DateTime.MinValue.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;
        private static readonly long _maxUnixTimestampMilliseconds = (DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;

        public static DateTime? ParseDate(string value)
        {
            value = Utils.GetString(value);

            // If is unix timestamp format. A positive number of exactly 10 digits is read as seconds, every other
            // integer (also a negative one, a date before 1970) as milliseconds, which is what the API returns.

            var digits = value.StartsWith('-') ? value.Substring(1) : value;
            if (digits.Length > 0 && digits.All(char.IsAsciiDigit))
            {
                if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number))
                {
                    return null;
                }

                if (number >= 1_000_000_000 && number <= 9_999_999_999)
                {
                    return DateTime.UnixEpoch.AddSeconds(number);
                }

                if (number < _minUnixTimestampMilliseconds || number > _maxUnixTimestampMilliseconds)
                {
                    return null;
                }

                return DateTime.UnixEpoch.AddMilliseconds(number);
            }

            // If on any of the available date formats. A value without a zone is read as UTC, like the formats below.

            if (DateTime.TryParseExact(value, _isoDateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var isoResult))
            {
                return isoResult;
            }

            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateResult))
            {
                return new DateTime(dateResult.Year, dateResult.Month, dateResult.Day, 0, 0, 0);
            }

            if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateResult))
            {
                return new DateTime(dateResult.Year, dateResult.Month, dateResult.Day, dateResult.Hour, dateResult.Minute, 0);
            }

            if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateResult))
            {
                return new DateTime(dateResult.Year, dateResult.Month, dateResult.Day, dateResult.Hour, dateResult.Minute, dateResult.Second);
            }

            if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateResult))
            {
                return dateResult;
            }

            return null;
        }

        public static bool ValidateIPv4(string ipString)
        {
            if (string.IsNullOrWhiteSpace(ipString))
            {
                return false;
            }

            var splitValues = ipString.Split('.');
            if (splitValues.Length != 4)
            {
                return false;
            }

            byte tempForParsing;

            return splitValues.All(r => byte.TryParse(r, out tempForParsing));
        }

        public static decimal Truncate(this decimal d, byte decimals)
        {
            decimal r = Math.Round(d, decimals);

            if (d > 0 && r > d)
            {
                return r - new decimal(1, 0, 0, false, decimals);
            }
            else if (d < 0 && r < d)
            {
                return r + new decimal(1, 0, 0, false, decimals);
            }

            return r;
        }

        public static bool IsValidRegex(string regex)
        {
            if (string.IsNullOrEmpty(regex))
                return false;

            try
            {
                Regex.Match("", regex);
            }
            catch (ArgumentException)
            {
                return false;
            }

            return true;
        }

        // A property's ValidationRegex is the owner's pattern but runs on request values, so a pattern that backtracks
        // badly (e.g. ^(\w+\s?)*$) must not keep a thread busy: a match that takes longer counts as no match.
        private static readonly TimeSpan _regexMatchTimeout = TimeSpan.FromMilliseconds(500);

        public static bool IsValidRegexMatch(string text, string regex)
        {
            if (string.IsNullOrEmpty(regex))
                return false;

            try
            {
                return Regex.IsMatch(text, regex, RegexOptions.None, _regexMatchTimeout);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        public static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            try
            {
                var addr = new MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
    }
}
