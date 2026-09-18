using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

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

            // Non-owner callers must never read the Users.Password column, even when a role's
            // property list includes it: it is a one-way hash with no legitimate client use.
            if (actionType == SecurityActionType.get &&
                type == SecurityTypes.Entity &&
                name.Equals(Globals.UsersEntityName, StringComparison.OrdinalIgnoreCase))
            {
                securityList = securityList.Select(WithoutPasswordProperty).ToList();
            }

            return securityList;
        }

        private static DBWS_Security WithoutPasswordProperty(DBWS_Security security)
        {
            var properties = security.GetProperties()
                .Where(p => !p.Trim().Equals(Globals.PasswordColumn, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Return a copy: the input objects belong to the cached application definition.
            return new DBWS_Security
            {
                Name = security.Name,
                TypeID = security.TypeID,
                RoleID = security.RoleID,
                Action = security.Action,
                Record = security.Record,
                Properties = string.Join(",", properties),
                RateLimit = security.RateLimit
            };
        }
    }
}
