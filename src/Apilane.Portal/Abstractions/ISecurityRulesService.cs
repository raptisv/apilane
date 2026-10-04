using Apilane.Common.Models;
using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The security page of an application the caller owns or collaborates on, and the one place
    /// that reads, checks and writes DBWS_Application.Security. The stored JSON keeps the shape the
    /// Razor page writes, so the API server and the Razor page read it as before. The one other
    /// writer is the schema import (ISchemaImportService), which appends rules unchecked, as the
    /// Razor import does.
    /// </summary>
    public interface ISecurityRulesService
    {
        /// <summary>
        /// Settings, roles, items and stored rules. When the API server cannot be asked for the
        /// roles of the application's users, the answer still comes, with RolesAvailable false.
        /// </summary>
        Task<SecurityResponse> GetAsync(string appToken);

        /// <summary>
        /// Saves the access settings, then resets the API server's cache.
        /// </summary>
        Task<SecuritySettingsResponse> UpdateSettingsAsync(string appToken, SecuritySettingsRequest request);

        /// <summary>
        /// Replaces every stored rule with the list sent, then resets the API server's cache. The
        /// first rule that is not acceptable stops the save with a VALIDATION error naming it
        /// (Rules[3].Action, ...).
        /// </summary>
        Task<SecurityRulesResponse> ReplaceRulesAsync(string appToken, SecurityRulesRequest request);

        /// <summary>
        /// The stored rules that can apply, read tolerantly (see SecurityResponse.Rules). The
        /// application must have its entities with their properties and its custom endpoints loaded.
        /// </summary>
        List<SecurityRuleResponse> ReadRules(DBWS_Application application);

        /// <summary>
        /// The rules as the Security column stores them. Throws a VALIDATION
        /// <see cref="Api.PortalException"/> for the first rule that is not acceptable; its
        /// property starts with <paramref name="path"/> ('Rules[3].Action'). The application must
        /// have its entities with their properties and its custom endpoints loaded.
        /// </summary>
        string SerializeRules(DBWS_Application application, IReadOnlyList<SecurityRuleRequest?> rules, string path);
    }
}
