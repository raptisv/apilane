using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    /// <summary>
    /// The security of an application: access settings and the rules that say which role may call
    /// what on its API server. Settings and rules are saved separately.
    /// </summary>
    [Route(RoutePrefix + "/security")]
    [AgentPermission("security")]
    public class ApplicationSecurityController : PortalApplicationApiControllerBase
    {
        private readonly ISecurityRulesService _securityRulesService;

        public ApplicationSecurityController(ISecurityRulesService securityRulesService)
        {
            _securityRulesService = securityRulesService;
        }

        /// <summary>
        /// Returns the access settings, the forgot-password links, the roles, the items rules can be
        /// written for (with the properties each action may list) and the stored rules. For the
        /// owner and collaborators. The roles of the application's users are read from the API
        /// server; when it cannot be asked the answer is still 200, with RolesAvailable false and
        /// only the built-in roles and the roles used in stored rules.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(SecurityResponse), StatusCodes.Status200OK)]
        public async Task<SecurityResponse> Get(string appToken)
        {
            return await _securityRulesService.GetAsync(appToken);
        }

        /// <summary>
        /// Saves the access settings, all together. Answers 400 VALIDATION when a value other than
        /// ClientIPs is missing (AuthTokenExpireMinutes, ForceSingleLogin, AllowLoginUnconfirmedEmail,
        /// AllowUserRegister, MaxAllowedFileSizeInKB, ClientIPsLogic), AuthTokenExpireMinutes is
        /// below 1, MaxAllowedFileSizeInKB is not between 1 and 25600, ClientIPsLogic is not Block or
        /// Allow, or with one error per entry of ClientIPs that is not an IPv4 address in plain
        /// dotted form (ClientIPs[2], ...); then nothing is saved. For the owner and
        /// collaborators. A 'Warning' response header means the settings were saved and the API
        /// server could not be refreshed: it keeps the old settings until its cache expires.
        /// </summary>
        [HttpPut("settings")]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(SecuritySettingsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<SecuritySettingsResponse> UpdateSettings(string appToken, SecuritySettingsRequest request)
        {
            return await _securityRulesService.UpdateSettingsAsync(appToken, request);
        }

        /// <summary>
        /// Replaces every rule of the application with the list sent; a rule left out is removed.
        /// The first rule that is not acceptable stops the save with 400 VALIDATION naming it in
        /// Errors (Rules[3].Action, ...): Type is not Entity, CustomEndpoint or Schema; Name is not
        /// an entity or custom endpoint of the application (case-sensitive) or, for Schema, not
        /// 'Schema'; RoleID is empty; Action is not get, post, put or delete, or not an action the
        /// item offers (Schema and custom endpoints: get only; an entity: see its Allow* in Items); Record is not All or Owned; Properties lists a name the item does not offer for
        /// the action (see Items); RateLimit has a TimeWindow other than Per_Second, Per_Minute or
        /// Per_Hour, or MaxRequests below 1; or the rule has the same Type, Name, RoleID and Action
        /// as an earlier one. Then nothing is saved. Returns the stored rules. For the owner and
        /// collaborators. A 'Warning' response header means the rules were saved and the API
        /// server could not be refreshed: it keeps the old rules until its cache expires.
        /// </summary>
        [HttpPut("rules")]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(SecurityRulesResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<SecurityRulesResponse> ReplaceRules(string appToken, SecurityRulesRequest request)
        {
            return await _securityRulesService.ReplaceRulesAsync(appToken, request);
        }
    }
}
