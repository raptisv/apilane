using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// Everything the security page of an application shows: the access settings, the two
    /// forgot-password links, the roles, the items rules can be written for and the stored rules.
    /// </summary>
    public class SecurityResponse
    {
        [Required]
        public SecuritySettingsResponse Settings { get; set; } = new SecuritySettingsResponse();

        [Required]
        public ForgotPasswordLinksResponse ForgotPasswordLinks { get; set; } = new ForgotPasswordLinksResponse();

        /// <summary>
        /// Anonymous and Authenticated first, then the roles used in stored rules, then the other
        /// roles the application's users have (read from the API server).
        /// </summary>
        [Required]
        public List<SecurityRoleResponse> Roles { get; set; } = new List<SecurityRoleResponse>();

        /// <summary>
        /// False when the API server could not be asked for the roles of the application's users:
        /// Roles then holds only the two built-in roles and the roles used in stored rules.
        /// </summary>
        [Required]
        public bool RolesAvailable { get; set; }

        /// <summary>
        /// What rules can be written for: Schema first, then the entities (system ones first, then
        /// by name), then the custom endpoints by name.
        /// </summary>
        [Required]
        public List<SecurityItemResponse> Items { get; set; } = new List<SecurityItemResponse>();

        /// <summary>
        /// The stored rules, in the order they are stored. A stored rule that can never apply is left
        /// out (unknown type or action, no name or role, an item that no longer exists, an action
        /// the item does not offer, such as anything but get on Schema or a custom endpoint, a
        /// second rule for the same type, name, role and action), so a
        /// PUT of this list stores only rules that apply.
        /// </summary>
        [Required]
        public List<SecurityRuleResponse> Rules { get; set; } = new List<SecurityRuleResponse>();
    }

    /// <summary>
    /// How users of the application sign in, register and upload files, and which IP addresses may
    /// call its API.
    /// </summary>
    public class SecuritySettingsResponse
    {
        /// <summary>
        /// How long an auth token stays valid, in minutes.
        /// </summary>
        [Required]
        public int AuthTokenExpireMinutes { get; set; }

        /// <summary>
        /// Force only one login at a time: a new login ends the user's other sessions.
        /// </summary>
        [Required]
        public bool ForceSingleLogin { get; set; }

        /// <summary>
        /// Allow users with unconfirmed email to login.
        /// </summary>
        [Required]
        public bool AllowLoginUnconfirmedEmail { get; set; }

        /// <summary>
        /// Allow new users to register.
        /// </summary>
        [Required]
        public bool AllowUserRegister { get; set; }

        /// <summary>
        /// The largest file that can be uploaded, in KB (1 to 25600).
        /// </summary>
        [Required]
        public int MaxAllowedFileSizeInKB { get; set; }

        /// <summary>
        /// Block: the addresses in ClientIPs are refused. Allow: only the addresses in ClientIPs are
        /// accepted. An empty ClientIPs accepts every address either way.
        /// </summary>
        [Required]
        public string ClientIPsLogic { get; set; } = string.Empty;

        /// <summary>
        /// IPv4 addresses, compared exactly with the caller's address.
        /// </summary>
        [Required]
        public List<string> ClientIPs { get; set; } = new List<string>();
    }

    /// <summary>
    /// New access settings of an application. Every value is written.
    /// </summary>
    public class SecuritySettingsRequest
    {
        /// <summary>
        /// How long an auth token stays valid, in minutes: 1 or more.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [Range(1, int.MaxValue, ErrorMessage = "Must be 1 or more")]
        public int? AuthTokenExpireMinutes { get; set; }

        /// <summary>
        /// Force only one login at a time: a new login ends the user's other sessions.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public bool? ForceSingleLogin { get; set; }

        /// <summary>
        /// Allow users with unconfirmed email to login.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public bool? AllowLoginUnconfirmedEmail { get; set; }

        /// <summary>
        /// Allow new users to register.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public bool? AllowUserRegister { get; set; }

        /// <summary>
        /// The largest file that can be uploaded, in KB: 1 to 25600 (25 MB).
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [Range(1, 25600, ErrorMessage = "Must be between 1 and 25600 (1 KB to 25 MB)")]
        public int? MaxAllowedFileSizeInKB { get; set; }

        /// <summary>
        /// Block or Allow.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [RegularExpression("^(Block|Allow)$", ErrorMessage = "Must be Block or Allow")]
        public string? ClientIPsLogic { get; set; }

        /// <summary>
        /// IPv4 addresses in plain dotted form: four numbers from 0 to 255, separated by dots, with
        /// no leading zeros, signs or spaces inside (for example 10.0.0.1). Each is trimmed and empty
        /// entries are dropped. Every entry that is not an address gets its own error
        /// (ClientIPs[2], ...). Null or left out stores an empty list.
        /// </summary>
        public List<string?>? ClientIPs { get; set; }
    }

    /// <summary>
    /// Where a user who forgot the password is sent, on the API server of the application. Both
    /// links send the password-reset e-mail, which needs the application's SMTP settings.
    /// </summary>
    public class ForgotPasswordLinksResponse
    {
        /// <summary>
        /// A page that asks for the e-mail address.
        /// </summary>
        [Required]
        public string PageUrl { get; set; } = string.Empty;

        /// <summary>
        /// The API endpoint to call instead; '{Email}' is to be replaced with the user's address.
        /// </summary>
        [Required]
        public string ApiUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// A role rules can be written for.
    /// </summary>
    public class SecurityRoleResponse
    {
        /// <summary>
        /// The value a rule stores: ANONYMOUS, AUTHENTICATED or the role's name.
        /// </summary>
        [Required]
        public string RoleID { get; set; } = string.Empty;

        [Required]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Anonymous (every caller, signed in or not), Authenticated (every signed-in user) or Role
        /// (users that have this role).
        /// </summary>
        [Required]
        public string Kind { get; set; } = string.Empty;

        /// <summary>
        /// A role used in stored rules that no user of the application has, or whose users could
        /// not be checked (RolesAvailable is false). Always false for Anonymous and Authenticated.
        /// </summary>
        [Required]
        public bool Orphaned { get; set; }
    }

    /// <summary>
    /// Something rules can be written for, and what its rules may contain.
    /// </summary>
    public class SecurityItemResponse
    {
        /// <summary>
        /// Entity, CustomEndpoint or Schema.
        /// </summary>
        [Required]
        public string Type { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// True for the entities every application has (Users, Files, ...); false for the other
        /// entities, Schema and custom endpoints.
        /// </summary>
        [Required]
        public bool IsSystem { get; set; }

        /// <summary>
        /// Whether records can be created. Every item can be read (get).
        /// </summary>
        [Required]
        public bool AllowPost { get; set; }

        [Required]
        public bool AllowPut { get; set; }

        [Required]
        public bool AllowDelete { get; set; }

        /// <summary>
        /// Whether the entity has an Owner column, which is what makes Record 'Owned' work.
        /// </summary>
        [Required]
        public bool HasOwner { get; set; }

        /// <summary>
        /// The properties a get rule may list: every property but the primary key, which is always returned.
        /// </summary>
        [Required]
        public List<string> PropertiesGet { get; set; } = new List<string>();

        /// <summary>
        /// The properties a post or put rule may list: the ones that can be edited. Empty for Files,
        /// whose records are made by uploads.
        /// </summary>
        [Required]
        public List<string> PropertiesPostPut { get; set; } = new List<string>();

        /// <summary>
        /// The property that ties each record to a record of the application's differentiation
        /// entity, or null. The API server filters on it. It is set by the API server, so it is
        /// never in PropertiesPostPut.
        /// </summary>
        public string? DifferentiationProperty { get; set; }
    }

    /// <summary>
    /// One stored rule: the role may call the action on the item.
    /// </summary>
    public class SecurityRuleResponse
    {
        /// <summary>
        /// Entity, CustomEndpoint or Schema.
        /// </summary>
        [Required]
        public string Type { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string RoleID { get; set; } = string.Empty;

        /// <summary>
        /// get, post, put or delete.
        /// </summary>
        [Required]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// All, or Owned: only the records the user owns.
        /// </summary>
        [Required]
        public string Record { get; set; } = string.Empty;

        /// <summary>
        /// The properties the role may read (get) or write (post, put), besides the primary key.
        /// Empty means none: a get returns only the primary key, a post or put is refused.
        /// </summary>
        [Required]
        public List<string> Properties { get; set; } = new List<string>();

        /// <summary>
        /// Null when the rule has no rate limit.
        /// </summary>
        public SecurityRateLimitResponse? RateLimit { get; set; }
    }

    /// <summary>
    /// At most MaxRequests calls per TimeWindow.
    /// </summary>
    public class SecurityRateLimitResponse
    {
        [Required]
        public int MaxRequests { get; set; }

        /// <summary>
        /// Per_Second, Per_Minute or Per_Hour.
        /// </summary>
        [Required]
        public string TimeWindow { get; set; } = string.Empty;
    }

    /// <summary>
    /// The stored rules of an application.
    /// </summary>
    public class SecurityRulesResponse
    {
        [Required]
        public List<SecurityRuleResponse> Rules { get; set; } = new List<SecurityRuleResponse>();
    }

    /// <summary>
    /// The complete list of rules of an application. It replaces every stored rule: a rule that
    /// is left out is removed.
    /// </summary>
    public class SecurityRulesRequest
    {
        /// <summary>
        /// Every rule the application should have after the call; an empty list removes them all.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public List<SecurityRuleRequest?>? Rules { get; set; }
    }

    /// <summary>
    /// One rule: the role may call the action on the item. The values are checked by the server,
    /// in the order they are listed here.
    /// </summary>
    public class SecurityRuleRequest
    {
        /// <summary>
        /// Entity, CustomEndpoint or Schema.
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// The name of an entity or a custom endpoint of the application (case-sensitive), or
        /// 'Schema' for the Schema item.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// ANONYMOUS, AUTHENTICATED or the name of a role, stored as sent.
        /// </summary>
        public string? RoleID { get; set; }

        /// <summary>
        /// get, post, put or delete, in any case; stored in lower case. Only an action the item
        /// offers: get always, post, put and delete only on an entity whose AllowPost, AllowPut or
        /// AllowDelete is true (see Items). Schema and custom endpoints allow only get.
        /// </summary>
        public string? Action { get; set; }

        /// <summary>
        /// All or Owned.
        /// </summary>
        public string? Record { get; set; }

        /// <summary>
        /// Entity rules: names from PropertiesGet of the item for get, from PropertiesPostPut for
        /// post and put; none for delete. Other items have no properties. Kept in the order sent.
        /// </summary>
        public List<string?>? Properties { get; set; }

        /// <summary>
        /// Null or left out for no rate limit.
        /// </summary>
        public SecurityRateLimitRequest? RateLimit { get; set; }
    }

    /// <summary>
    /// At most MaxRequests calls per TimeWindow.
    /// </summary>
    public class SecurityRateLimitRequest
    {
        /// <summary>
        /// 1 or more.
        /// </summary>
        public int MaxRequests { get; set; }

        /// <summary>
        /// Per_Second, Per_Minute or Per_Hour.
        /// </summary>
        public string? TimeWindow { get; set; }
    }
}
