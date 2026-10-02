using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Services
{
    public static class EntityAccess
    {
        /// <summary>
        /// Returns full access description to entity/endpoint
        /// </summary>
        public static List<DBWS_Security> GetFull(
            string name,
            List<DBWS_EntityProperty> properties,
            SecurityActionType actionType)
        {
            return new List<DBWS_Security>()
            {
                new()
                {
                    Action = actionType.ToString(),
                    Name = name,
                    Record = (int)EndpointRecordAuthorization.All,
                    Properties = string.Join(",", properties.Select(x => x.Name)),
                    RateLimit = null // Not rate limited
                }
            };
        }

        /// <summary>
        /// Returns maximum access description to entity/endpoint for the current user
        /// </summary>
        public static List<DBWS_Security> GetMaximum(
            Users? user,
            List<DBWS_Security> applicationSecurityList,
            string name,
            SecurityTypes type,
            SecurityActionType actionType)
        {
            // Get all security records that apply to this Entity and this Action
            var entitySecurity = applicationSecurityList.Where(x => 
                x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                x.Action.Equals(actionType.ToString(), StringComparison.OrdinalIgnoreCase) &&
                x.TypeID_Enum == type)
            .ToList();

            var securityList = new List<DBWS_Security>();

            // If access is specifically set
            if (entitySecurity.Count > 0)
            {
                if (entitySecurity.Any(x => x.RoleID.Equals(Globals.ANONYMOUS))) // Allow anonymous
                {
                    securityList.AddRange(entitySecurity.Where(x => x.RoleID.Equals(Globals.ANONYMOUS)).ToList());
                }

                if (user is not null) // If the user is authenticated 
                {
                    if (entitySecurity.Any(x => x.RoleID.Equals(Globals.AUTHENTICATED))) // If there is a security for authorized users
                    {
                        securityList.AddRange(entitySecurity.Where(x => x.RoleID.Equals(Globals.AUTHENTICATED)));
                    }

                    if (entitySecurity.Any(x => user.GetRoles().Any(r => r.Equals(x.RoleID)))) // If the user has roles and there is security for that specific role
                    {
                        securityList.AddRange(entitySecurity.Where(x => user.GetRoles().Any(r => r.Equals(x.RoleID))));
                    }
                }
            }

            return securityList;
        }

        /// <summary>
        /// Applies the rate limit of the access rules that let this caller in (the result of
        /// <see cref="GetMaximum"/> or <see cref="GetFull"/>), and throws RATE_LIMIT_EXCEEDED when it is
        /// used up. Called wherever access to an entity, endpoint or the schema is decided, so that a
        /// request counts whichever way it arrives: by id, as statistics, as a file, or as one operation
        /// of a transaction. When several rules apply, the most permissive one wins, and a rule without
        /// a limit means no limit.
        /// </summary>
        public static async Task EnforceRateLimitAsync(
            string appToken,
            Users? user,
            List<DBWS_Security> userSecurity,
            string name,
            SecurityTypes type,
            SecurityActionType actionType)
        {
            if (!userSecurity.Select(x => x.RateLimit).IsRateLimited(out int maxRequests, out TimeSpan timeWindow))
            {
                return;
            }

            var permitted = await ApplicationRateLimiter.GetOrCreate(appToken).TryAcquireAsync(
                maxRequests,
                timeWindow,
                user?.ID.ToString(),
                GetRateLimitName(name, type),
                actionType.ToString(),
                default);

            if (!permitted)
            {
                throw new ApilaneException(AppErrors.RATE_LIMIT_EXCEEDED);
            }
        }

        /// <summary>
        /// The name requests are counted under. Entities, custom endpoints and the schema are named
        /// independently (an entity and an endpoint may share a name) and have separate rules, so they
        /// are counted separately too.
        /// </summary>
        public static string GetRateLimitName(string name, SecurityTypes type)
        {
            return type == SecurityTypes.Entity
                ? name
                : $"{type}/{name}";
        }
    }
}
