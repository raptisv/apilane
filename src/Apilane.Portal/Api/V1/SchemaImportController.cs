using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    /// <summary>
    /// Schema import: adds entities, properties, constraints, security rules and custom endpoints
    /// to an application in one call.
    /// </summary>
    [Route(RoutePrefix + "/schema-import")]
    [AgentPermission("schema")]
    public class SchemaImportController : PortalApplicationApiControllerBase
    {
        private readonly ISchemaImportService _schemaImportService;

        public SchemaImportController(ISchemaImportService schemaImportService)
        {
            _schemaImportService = schemaImportService;
        }

        /// <summary>
        /// Returns everything the Source application has and this one lacks, as the body POST
        /// schema-import accepts: it can be posted back unchanged. Names are matched whatever their
        /// letter case. Entities: the custom entities this application does not have (IsNew true,
        /// with all their custom properties and constraints) and the ones it has that lack some
        /// property or constraint (IsNew false, with only what is missing). Security: the rules of
        /// entities and custom endpoints the source still has, when this application has no rule
        /// with the same type, name, role and action; a rule it has with other values is left
        /// out, and Schema rules are never listed. Each rule is what the import accepts for this
        /// application as the import leaves it: a rule whose action its item does not offer is
        /// left out, and so is a property the application would lack (a custom property of Users,
        /// which Entities does not list) from the Properties of a rule. CustomEndpoints: the ones
        /// this application has no endpoint of that name for. All three lists are empty when
        /// nothing is missing. For the owner and collaborators of both applications: answers 404
        /// NOT_FOUND when the caller cannot see the source, 400 VALIDATION on Source when it is
        /// this application, and 409 CONFLICT when the stored security rules of either
        /// application cannot be read. Nothing is changed and the API server is not called.
        /// </summary>
        [HttpGet("diff")]
        [AgentPermission("entities")]
        [AgentPermission("security")]
        [AgentPermission("custom-endpoints")]
        [ProducesResponseType(typeof(SchemaImportRequest), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        public async Task<SchemaImportRequest> GetDiff(
            string appToken,
            [FromQuery(Name = "Source")][Required(ErrorMessage = "Required")] string source)
        {
            return await _schemaImportService.GetDiffAsync(appToken, source);
        }

        /// <summary>
        /// Adds what the body lists to the application. NOT ATOMIC: the items are applied one by
        /// one, each on the API server and in the Portal; when one fails the import stops there,
        /// everything applied before it stays, and the error Message names the step that failed.
        /// Sending the same body again is safe: what was applied is then skipped with a warning.
        /// Order: the entities (see Entities for their order), each with its properties and then
        /// its constraints; then the security rules; then the custom endpoints; last one reset of
        /// the API server's cache.
        /// An item the application already has (names matched whatever their letter case) is
        /// skipped and reported in Warnings, provided it does not differ: 400 VALIDATION naming the
        /// value in Errors (Entities[0].Properties[2].TypeID, ...) when an existing entity has
        /// another RequireChangeTracking or HasDifferentiationProperty, an existing property
        /// another TypeID, Required, Encrypted, DecimalPlaces, Maximum, Minimum or ValidationRegex,
        /// an existing foreign key of the same property another target or on-delete action, or an
        /// existing security rule (same type, name, role and action) another record scope,
        /// property list or rate limit. An existing custom endpoint is skipped whatever its query.
        /// Also 400 VALIDATION, before anything is applied: a missing Name, RoleID, Action or
        /// Query, a constraint TypeID other than 1 or 2, a constraint with IsSystem true, a null
        /// list item, a foreign key that is not 'Property,Entity[,ON_DELETE_x]', a rate limit
        /// with MaxRequests below 1 or a TimeWindowType other than 0 to 3, the name of a new
        /// entity, property or custom endpoint that its own create endpoint would refuse, a new
        /// property with an unknown TypeID, and every security rule that PUT security/rules would
        /// refuse, one error for each rule with its place (Security[2].Action): a TypeID other than
        /// 0 to 2, a Record other than 0 or 1, a Name that is neither an entity nor a custom
        /// endpoint that the application or the body has (whatever its letter case; Schema for
        /// TypeID 2), an Action the item does not offer, a property the item does not have for that
        /// action (property names are matched exactly), a second rule for the same type, name,
        /// role and action that has other values than the first. Nothing else is checked: the
        /// values of a new property go to the API server as sent, and a security rule is stored as
        /// sent, with any role, but under the name its entity or custom endpoint has. Before
        /// anything is applied too: 403 FORBIDDEN when a caller who is not an administrator lists
        /// a constraint for a system entity (Users, Files), and 409 CONFLICT when the body has
        /// security rules and the stored ones cannot be read. A refusal of the API server is 400
        /// VALIDATION with its message and the item it was for, any other failure of it 502
        /// UPSTREAM_ERROR. For the owner and collaborators. A 'Warning' response header means the
        /// import went through and the API server could not be refreshed afterwards.
        /// </summary>
        [HttpPost]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(SchemaImportResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<SchemaImportResponse> Import(string appToken, SchemaImportRequest request)
        {
            return await _schemaImportService.ImportAsync(appToken, request);
        }
    }
}
