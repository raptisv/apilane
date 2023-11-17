using Apilane.Common.Enums;
using Apilane.Common.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Apilane.Common.Models
{
    public class DBWS_Security
    {
        public string Name { get; set; } = null!;

        /// <summary>
        /// <see cref="SecurityTypes"/>
        /// </summary>
        public int TypeID { get; set; }
        [JsonIgnore]
        public SecurityTypes TypeID_Enum { get { return (SecurityTypes)TypeID; } }
        public string RoleID { get; set; } = null!;
        public string Action { get; set; } = null!;

        /// <summary>
        /// <see cref="SecurityTypes"/>
        /// </summary>
        public int Record { get; set; }
        public string? Properties { get; set; }
        public List<string> GetProperties()
        {
            return Utils.GetString(Properties).Split(',').Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }

        public RateLimitItem? RateLimit { get; set; }

        public class RateLimitItem
        {
            public int MaxRequests { get; set; }
            public int TimeWindowType { get; set; }
            public TimeSpan TimeWindow
            {
                get
                {
                    return TimeWindowType switch
                    {
                        (int)EndpointRateLimit.Per_Second => TimeSpan.FromSeconds(1),
                        (int)EndpointRateLimit.Per_Minute => TimeSpan.FromMinutes(1),
                        (int)EndpointRateLimit.Per_Hour => TimeSpan.FromHours(1),
                        _ => TimeSpan.Zero,
                    };
                }
            }

            public double RequestPerSecond()
            {
                return MaxRequests / TimeWindow.TotalSeconds;
            }

            public static RateLimitItem New(int maxRequests, EndpointRateLimit timeWindowType) => new()
            {
                MaxRequests = maxRequests,
                TimeWindowType = (int)timeWindowType
            };

            public string ToUniqueString() => $"{MaxRequests} request {EnumProvider<EndpointRateLimit>.GetDisplayValue((EndpointRateLimit)TimeWindowType)}".ToLower();
        }


        public string NameDescriptive() => $"{TypeID_Enum.ToString()} {Name} - {RoleID} {Action}";
        public string ToUniqueStringShort() => $"{TypeID}_{Name}_{RoleID}_{Action}";
        public string ToUniqueStringLong() => $"{TypeID}_{Name}_{RoleID}_{Action}_{Record}_{string.Join(",", GetProperties().OrderBy(x => x))}_{RateLimit?.ToUniqueString()}";
    }
}
