using Apilane.Common.Enums;
using Apilane.Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Common.Extensions
{
    public static class SecurityExtensions
    {
        public static bool IsRateLimited(
            this IEnumerable<DBWS_Security.RateLimitItem?> securitiesList,
            out int maxRequests,
            out TimeSpan timeWindow)
        {
            maxRequests = int.MaxValue;
            timeWindow = TimeSpan.Zero;

            if (securitiesList.Count() == 0 ||
                securitiesList.Any(x => x is null) ||
                securitiesList.Any(x => x!.TimeWindowType == (int)EndpointRateLimit.None))
            {
                // If there is no security or any security is not rate limited, we exit here, the request is not rate limited.
                return false;
            }

            // Find the most "wide" rate limit rule
            var maxRateLimitSecurity = securitiesList
                .Where(x => x is not null)
                .OrderByDescending(x => x?.RequestPerSecond() ?? 0)
                .First();

            maxRequests = maxRateLimitSecurity!.MaxRequests;
            timeWindow = maxRateLimitSecurity.TimeWindow;
            return true;
        }

        public static string BuildRateLimitingGrainKeyExt(
            int maxRequests,
            TimeSpan timeWindow,
            string? userIdentifier,
            string entityOrEndpoint,
            SecurityActionType securityActionType)
            => $"{maxRequests}_{timeWindow.ToString(@"hh\:mm\:ss")}_{userIdentifier}:{entityOrEndpoint}:{securityActionType}";
    }
}
