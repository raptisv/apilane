using System;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Apilane.Common
{
    public static class Utils
    {
        public static readonly Random _random = new Random(DateTime.Now.Millisecond);

        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length).Select(s => s[_random.Next(s.Length)]).ToArray());
        }

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

        public static DateTime? ParseDate(string value)
        {
            value = Utils.GetString(value);

            /// If is unix timestamp format

            if (value.All(char.IsDigit))
            {
                long number = Utils.GetLong(value, 0);

                if (number >= 0)
                {
                    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    switch (number.ToString().Length)
                    {
                        case 10:
                            return epoch.AddSeconds(number);
                        case 13:
                            return epoch.AddMilliseconds(number);
                    }
                }
            }

            // If on any of the available date formats

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

        public static bool IsValidRegexMatch(string text, string regex)
        {
            if (string.IsNullOrEmpty(regex))
                return false;

            try
            {
                return Regex.Match(text, regex).Success;
            }
            catch (ArgumentException)
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
