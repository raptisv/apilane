using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class SecurityRulesService : ISecurityRulesService
    {
        private const string FilesEntityName = "Files";
        private const string RulesPath = nameof(SecurityRulesRequest.Rules);

        private static readonly string[] _actions = { "get", "post", "put", "delete" };

        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IApiServerClient _apiServerClient;
        private readonly IApplicationWriteScope _applicationWriteScope;
        private readonly ILogger<SecurityRulesService> _logger;

        public SecurityRulesService(
            IApplicationAccessService applicationAccessService,
            IApiServerClient apiServerClient,
            IApplicationWriteScope applicationWriteScope,
            ILogger<SecurityRulesService> logger)
        {
            _applicationAccessService = applicationAccessService;
            _apiServerClient = apiServerClient;
            _applicationWriteScope = applicationWriteScope;
            _logger = logger;
        }

        public async Task<SecurityResponse> GetAsync(string appToken)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var stored = ParseStored(application);

            List<string>? userRoles = null;

            try
            {
                userRoles = await _apiServerClient.GetUserRolesAsync(application);
            }
            catch (PortalException exception)
            {
                // The Razor page fails as a whole here; the rest of the page does not need the API server.
                _logger.LogWarning(exception, "Roles of the application's users could not be read | Application {Token}", application.Token);
            }

            var serverUrl = application.Server.ServerUrl.ToLowerInvariant();

            return new SecurityResponse
            {
                Settings = ToSettings(application),
                ForgotPasswordLinks = new ForgotPasswordLinksResponse
                {
                    PageUrl = $"{serverUrl}/App/{application.Token}/Account/Manage/ForgotPassword",
                    ApiUrl = $"{serverUrl}/api/Email/ForgotPassword?AppToken={application.Token}&Email={{Email}}"
                },
                Roles = ToRoles(stored, userRoles),
                RolesAvailable = userRoles is not null,
                Items = GetItems(application),
                Rules = ReadRules(application, stored)
            };
        }

        public async Task<SecuritySettingsResponse> UpdateSettingsAsync(string appToken, SecuritySettingsRequest request)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            var addresses = new List<string>();
            var errors = new List<ErrorDetail>();
            var sent = request.ClientIPs ?? new List<string?>();

            for (var i = 0; i < sent.Count; i++)
            {
                var address = sent[i]?.Trim();

                // Empty entries are dropped, as the Razor page drops them.
                if (string.IsNullOrEmpty(address))
                {
                    continue;
                }

                // The API server compares addresses as text, so only the plain dotted form can ever
                // match: the round trip refuses signs, inner spaces and leading zeros.
                if (Utils.ValidateIPv4(address) && IPAddress.TryParse(address, out var ip) && ip.ToString() == address)
                {
                    addresses.Add(address);
                }
                else
                {
                    errors.Add(new ErrorDetail
                    {
                        Property = $"{nameof(SecuritySettingsRequest.ClientIPs)}[{i}]",
                        Message = $"'{address}' is not a valid IPv4 address"
                    });
                }
            }

            if (errors.Count > 0)
            {
                throw PortalException.Validation(errors);
            }

            // [Required] on the contract has already refused a missing value.
            application.AuthTokenExpireMinutes = request.AuthTokenExpireMinutes.GetValueOrDefault();
            application.ForceSingleLogin = request.ForceSingleLogin.GetValueOrDefault();
            application.AllowLoginUnconfirmedEmail = request.AllowLoginUnconfirmedEmail.GetValueOrDefault();
            application.AllowUserRegister = request.AllowUserRegister.GetValueOrDefault();
            application.MaxAllowedFileSizeInKB = request.MaxAllowedFileSizeInKB.GetValueOrDefault();
            application.ClientIPsLogic = request.ClientIPsLogic == nameof(AppClientIPsLogics.Allow)
                ? (int)AppClientIPsLogics.Allow
                : (int)AppClientIPsLogics.Block;
            application.ClientIPsValue = string.Join(",", addresses);
            application.DateModified = DateTime.UtcNow;

            // Nothing to call: the cache reset is how the API server learns about the change.
            await _applicationWriteScope.SaveAsync(application);

            return ToSettings(application);
        }

        public async Task<SecurityRulesResponse> ReplaceRulesAsync(string appToken, SecurityRulesRequest request)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);

            // [Required] on the contract already answers 400 for a missing list.
            var rules = request.Rules ?? throw PortalException.Validation(RulesPath, "Required");

            application.Security = SerializeRules(application, rules, RulesPath);
            application.DateModified = DateTime.UtcNow;

            await _applicationWriteScope.SaveAsync(application);

            return new SecurityRulesResponse { Rules = ReadRules(application) };
        }

        public List<SecurityRuleResponse> ReadRules(DBWS_Application application)
        {
            return ReadRules(application, ParseStored(application));
        }

        public string SerializeRules(DBWS_Application application, IReadOnlyList<SecurityRuleRequest?> rules, string path)
        {
            var stored = new List<DBWS_Security>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < rules.Count; i++)
            {
                var rulePath = $"{path}[{i}]";
                var rule = ToStored(application, rules[i], rulePath);
                var key = Key(rule.TypeID, rule.Name, rule.RoleID, rule.Action);

                if (seen.TryGetValue(key, out var first))
                {
                    throw PortalException.Validation(rulePath, $"Same type, name, role and action as {path}[{first}]");
                }

                seen.Add(key, i);
                stored.Add(rule);
            }

            // The same class and serializer as the Razor page, so the stored JSON is the same.
            return JsonSerializer.Serialize(stored);
        }

        /// <summary>
        /// The rule as the Security column stores it. Throws for the first value that is not acceptable.
        /// </summary>
        private static DBWS_Security ToStored(DBWS_Application application, SecurityRuleRequest? rule, string path)
        {
            if (rule is null)
            {
                throw PortalException.Validation(path, "Required");
            }

            SecurityTypes type = rule.Type switch
            {
                nameof(SecurityTypes.Entity) => SecurityTypes.Entity,
                nameof(SecurityTypes.CustomEndpoint) => SecurityTypes.CustomEndpoint,
                nameof(SecurityTypes.Schema) => SecurityTypes.Schema,
                _ => throw PortalException.Validation($"{path}.{nameof(SecurityRuleRequest.Type)}", "Must be Entity, CustomEndpoint or Schema")
            };

            var namePath = $"{path}.{nameof(SecurityRuleRequest.Name)}";
            var name = rule.Name;

            if (string.IsNullOrWhiteSpace(name))
            {
                throw PortalException.Validation(namePath, "Required");
            }

            DBWS_Entity? entity = null;

            switch (type)
            {
                case SecurityTypes.Entity:
                    entity = FindEntity(application, name)
                        ?? throw PortalException.Validation(namePath, $"The application has no entity '{name}'");
                    break;
                case SecurityTypes.CustomEndpoint:
                    if (!HasCustomEndpoint(application, name))
                    {
                        throw PortalException.Validation(namePath, $"The application has no custom endpoint '{name}'");
                    }
                    break;
                default:
                    if (name != Globals.SCHEMA)
                    {
                        throw PortalException.Validation(namePath, $"Must be {Globals.SCHEMA}");
                    }
                    break;
            }

            var roleId = rule.RoleID;

            if (string.IsNullOrWhiteSpace(roleId))
            {
                throw PortalException.Validation($"{path}.{nameof(SecurityRuleRequest.RoleID)}", "Required");
            }

            var actionPath = $"{path}.{nameof(SecurityRuleRequest.Action)}";
            var action = (rule.Action ?? string.Empty).ToLowerInvariant();

            if (!_actions.Contains(action))
            {
                throw PortalException.Validation(actionPath, "Must be get, post, put or delete");
            }

            if (type == SecurityTypes.Schema && action != "get")
            {
                throw PortalException.Validation(actionPath, "Schema rules allow only get");
            }

            if (!ItemAllows(type, entity, action))
            {
                throw PortalException.Validation(actionPath, $"{name} does not allow {action}");
            }

            EndpointRecordAuthorization record = rule.Record switch
            {
                nameof(EndpointRecordAuthorization.All) => EndpointRecordAuthorization.All,
                nameof(EndpointRecordAuthorization.Owned) => EndpointRecordAuthorization.Owned,
                _ => throw PortalException.Validation($"{path}.{nameof(SecurityRuleRequest.Record)}", "Must be All or Owned")
            };

            var properties = rule.Properties ?? new List<string?>();
            var allowed = entity is null ? new List<string>() : AllowedProperties(application, entity, action);

            foreach (var property in properties)
            {
                if (property is null || !allowed.Contains(property, StringComparer.Ordinal))
                {
                    throw PortalException.Validation(
                        $"{path}.{nameof(SecurityRuleRequest.Properties)}",
                        entity is null
                            ? "Only entity rules have properties"
                            : $"'{property}' is not a property a {action} rule of {entity.Name} can list");
                }
            }

            DBWS_Security.RateLimitItem? rateLimit = null;

            if (rule.RateLimit is not null)
            {
                var rateLimitPath = $"{path}.{nameof(SecurityRuleRequest.RateLimit)}";

                EndpointRateLimit window = rule.RateLimit.TimeWindow switch
                {
                    nameof(EndpointRateLimit.Per_Second) => EndpointRateLimit.Per_Second,
                    nameof(EndpointRateLimit.Per_Minute) => EndpointRateLimit.Per_Minute,
                    nameof(EndpointRateLimit.Per_Hour) => EndpointRateLimit.Per_Hour,
                    _ => throw PortalException.Validation($"{rateLimitPath}.{nameof(SecurityRateLimitRequest.TimeWindow)}", "Must be Per_Second, Per_Minute or Per_Hour")
                };

                if (rule.RateLimit.MaxRequests < 1)
                {
                    throw PortalException.Validation($"{rateLimitPath}.{nameof(SecurityRateLimitRequest.MaxRequests)}", "Must be 1 or more");
                }

                rateLimit = DBWS_Security.RateLimitItem.New(rule.RateLimit.MaxRequests, window);
            }

            return new DBWS_Security
            {
                Name = name,
                TypeID = (int)type,
                Action = action,
                RoleID = roleId,
                Record = (int)record,
                // Entity rules store their list, empty included; other rules store none (as the Razor page does).
                Properties = entity is null ? null : string.Join(",", properties),
                RateLimit = rateLimit
            };
        }

        /// <summary>
        /// The stored rules that can apply, as the API shows them. Tolerant: the Razor page and older
        /// versions stored the action in any case, and anything the API server ignores is left out
        /// instead of failing the whole answer.
        /// </summary>
        private static List<SecurityRuleResponse> ReadRules(DBWS_Application application, List<DBWS_Security> stored)
        {
            var result = new List<SecurityRuleResponse>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rule in stored)
            {
                if (string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.RoleID) || string.IsNullOrWhiteSpace(rule.Action))
                {
                    continue;
                }

                var action = rule.Action.ToLowerInvariant();

                if (!_actions.Contains(action) || !Enum.IsDefined(typeof(SecurityTypes), rule.TypeID))
                {
                    continue;
                }

                var type = (SecurityTypes)rule.TypeID;
                DBWS_Entity? entity = null;

                // Rules of items that are gone are not shown; the Razor page drops them on its next save too.
                if (type == SecurityTypes.Entity)
                {
                    entity = FindEntity(application, rule.Name);

                    if (entity is null)
                    {
                        continue;
                    }
                }
                else if (type == SecurityTypes.CustomEndpoint && !HasCustomEndpoint(application, rule.Name))
                {
                    continue;
                }
                else if (type == SecurityTypes.Schema && rule.Name != Globals.SCHEMA)
                {
                    continue;
                }

                // A rule for an action the item does not offer has no cell; the Razor page drops it on its next save.
                if (!ItemAllows(type, entity, action))
                {
                    continue;
                }

                // The first of two rules for the same cell wins, as on the Razor page.
                if (!seen.Add(Key(rule.TypeID, rule.Name, rule.RoleID, action)))
                {
                    continue;
                }

                var allowed = entity is null ? new List<string>() : AllowedProperties(application, entity, action);

                var rateLimit = rule.RateLimit;
                var window = (EndpointRateLimit)(rateLimit?.TimeWindowType ?? (int)EndpointRateLimit.None);

                result.Add(new SecurityRuleResponse
                {
                    Type = type.ToString(),
                    Name = rule.Name,
                    RoleID = rule.RoleID,
                    Action = action,
                    // The API server treats anything but Owned as all records.
                    Record = rule.Record == (int)EndpointRecordAuthorization.Owned
                        ? nameof(EndpointRecordAuthorization.Owned)
                        : nameof(EndpointRecordAuthorization.All),
                    // Compared as stored, untrimmed, as the API server compares them.
                    Properties = rule.GetProperties()
                        .Where(x => allowed.Contains(x, StringComparer.Ordinal))
                        .Distinct(StringComparer.Ordinal)
                        .ToList(),
                    // A window the API server does not know never limits anything.
                    RateLimit = rateLimit is not null && window != EndpointRateLimit.None && Enum.IsDefined(window)
                        ? new SecurityRateLimitResponse { MaxRequests = rateLimit.MaxRequests, TimeWindow = window.ToString() }
                        : null
                });
            }

            return result;
        }

        /// <summary>
        /// Every rule of the Security column that can be read, one by one: a rule that does not
        /// have the stored shape is left out instead of failing the page.
        /// </summary>
        private List<DBWS_Security> ParseStored(DBWS_Application application)
        {
            var result = new List<DBWS_Security>();

            if (string.IsNullOrWhiteSpace(application.Security))
            {
                return result;
            }

            JsonNode? root;

            try
            {
                root = JsonNode.Parse(application.Security);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Stored security rules could not be read | Application {Token}", application.Token);
                return result;
            }

            if (root is not JsonArray items)
            {
                _logger.LogWarning("Stored security rules are not a list | Application {Token}", application.Token);
                return result;
            }

            foreach (var item in items)
            {
                if (item is not JsonObject)
                {
                    continue;
                }

                try
                {
                    var rule = item.Deserialize<DBWS_Security>();

                    if (rule is not null)
                    {
                        result.Add(rule);
                    }
                }
                catch (JsonException exception)
                {
                    _logger.LogWarning(exception, "A stored security rule could not be read and is left out | Application {Token}", application.Token);
                }
            }

            return result;
        }

        private static SecuritySettingsResponse ToSettings(DBWS_Application application)
        {
            return new SecuritySettingsResponse
            {
                AuthTokenExpireMinutes = application.AuthTokenExpireMinutes,
                ForceSingleLogin = application.ForceSingleLogin,
                AllowLoginUnconfirmedEmail = application.AllowLoginUnconfirmedEmail,
                AllowUserRegister = application.AllowUserRegister,
                MaxAllowedFileSizeInKB = application.MaxAllowedFileSizeInKB,
                ClientIPsLogic = application.ClientIPsLogic == (int)AppClientIPsLogics.Allow
                    ? nameof(AppClientIPsLogics.Allow)
                    : nameof(AppClientIPsLogics.Block),
                ClientIPs = (application.ClientIPsValue ?? string.Empty)
                    .Split(',')
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList()
            };
        }

        // The rows of the Razor grid: the two built-in roles, then the roles of stored rules, then the users' roles.
        private static List<SecurityRoleResponse> ToRoles(List<DBWS_Security> stored, List<string>? userRoles)
        {
            var roles = new List<SecurityRoleResponse>
            {
                new SecurityRoleResponse { RoleID = Globals.ANONYMOUS, DisplayName = "Anonymous users", Kind = "Anonymous" },
                new SecurityRoleResponse { RoleID = Globals.AUTHENTICATED, DisplayName = "Authenticated users", Kind = "Authenticated" }
            };

            var names = stored
                .Select(x => x.RoleID)
                .Concat(userRoles ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x) && x != Globals.ANONYMOUS && x != Globals.AUTHENTICATED)
                .Distinct(StringComparer.Ordinal);

            roles.AddRange(names.Select(x => new SecurityRoleResponse
            {
                RoleID = x,
                DisplayName = x,
                Kind = "Role",
                Orphaned = userRoles is null || !userRoles.Contains(x, StringComparer.Ordinal)
            }));

            return roles;
        }

        // The tabs of the Razor page, in its order.
        private static List<SecurityItemResponse> GetItems(DBWS_Application application)
        {
            var items = new List<SecurityItemResponse>
            {
                new SecurityItemResponse { Type = nameof(SecurityTypes.Schema), Name = Globals.SCHEMA }
            };

            items.AddRange(application.Entities
                .OrderByDescending(x => x.IsSystem)
                .ThenBy(x => x.Name)
                .Select(x => new SecurityItemResponse
                {
                    Type = nameof(SecurityTypes.Entity),
                    Name = x.Name,
                    IsSystem = x.IsSystem,
                    AllowPost = x.AllowPost(),
                    AllowPut = x.AllowPut(),
                    AllowDelete = x.AllowDelete(),
                    HasOwner = x.HasOwnerColumn(),
                    PropertiesGet = AllowedProperties(application, x, "get"),
                    PropertiesPostPut = AllowedProperties(application, x, "post"),
                    DifferentiationProperty = !string.IsNullOrWhiteSpace(application.DifferentiationEntity) && x.HasDifferentiationProperty
                        ? application.DifferentiationEntity.GetDifferentiationPropertyName()
                        : null
                }));

            items.AddRange((application.CustomEndpoints ?? new List<DBWS_CustomEndpoint>())
                .OrderBy(x => x.Name)
                .Select(x => new SecurityItemResponse { Type = nameof(SecurityTypes.CustomEndpoint), Name = x.Name }));

            return items;
        }

        /// <summary>
        /// The properties the Razor grid offers for an action of an entity: never the primary key,
        /// only editable ones for post and put, none for delete and none for a post to Files.
        /// </summary>
        private static List<string> AllowedProperties(DBWS_Application application, DBWS_Entity entity, string action)
        {
            var properties = entity.Properties.OrderBy(x => x.ID).Where(x => !x.IsPrimaryKey);

            return action switch
            {
                "get" => properties.Select(x => x.Name).ToList(),
                // Put is never allowed on Files, and its post takes a file, not properties.
                "post" or "put" when entity.Name != FilesEntityName => properties
                    .Where(x => x.AllowEdit(application.DifferentiationEntity, entity.HasDifferentiationProperty))
                    .Select(x => x.Name)
                    .ToList(),
                _ => new List<string>()
            };
        }

        /// <summary>
        /// Whether the item has the action, as the Razor grid draws its columns: every item has get;
        /// only entities have post, put and delete, and only when the entity allows them.
        /// </summary>
        private static bool ItemAllows(SecurityTypes type, DBWS_Entity? entity, string action)
        {
            if (action == "get")
            {
                return true;
            }

            if (type != SecurityTypes.Entity || entity is null)
            {
                return false;
            }

            return action switch
            {
                "post" => entity.AllowPost(),
                "put" => entity.AllowPut(),
                "delete" => entity.AllowDelete(),
                _ => false
            };
        }

        private static DBWS_Entity? FindEntity(DBWS_Application application, string name)
        {
            return application.Entities.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
        }

        private static bool HasCustomEndpoint(DBWS_Application application, string name)
        {
            return (application.CustomEndpoints ?? new List<DBWS_CustomEndpoint>())
                .Any(x => string.Equals(x.Name, name, StringComparison.Ordinal));
        }

        private static string Key(int typeId, string name, string roleId, string action)
        {
            return $"{typeId}|{name}|{roleId}|{action}";
        }
    }
}
