using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/entities/{entity}/properties")]
    [AgentPermission("entities")]
    public class PropertiesController : PortalApplicationApiControllerBase
    {
        private readonly IPropertyManagementService _propertyManagementService;

        public PropertiesController(IPropertyManagementService propertyManagementService)
        {
            _propertyManagementService = propertyManagementService;
        }

        /// <summary>
        /// Lists the properties of an entity: the primary key first, then the custom ones, then
        /// the system ones (IsSystem tells them apart), each group by name. For the owner and
        /// collaborators. Entity names are case-sensitive: 404 NOT_FOUND (Entity) for any other spelling.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ListResponse<PropertyResponse>), StatusCodes.Status200OK)]
        public async Task<ListResponse<PropertyResponse>> List(string appToken, string entity)
        {
            return new ListResponse<PropertyResponse>(await _propertyManagementService.GetAllAsync(appToken, entity));
        }

        /// <summary>
        /// Returns one property. The name is case-sensitive: 404 NOT_FOUND (Property) for any
        /// other spelling.
        /// </summary>
        [HttpGet("{property}")]
        [ProducesResponseType(typeof(PropertyResponse), StatusCodes.Status200OK)]
        public async Task<PropertyResponse> Get(string appToken, string entity, string property)
        {
            return await _propertyManagementService.GetAsync(appToken, entity, property);
        }

        /// <summary>
        /// Adds a custom property to an entity: its column on the API server. Values that do not
        /// apply to the type are ignored (Minimum and Maximum are for String and Number,
        /// DecimalPlaces for Number, ValidationRegex and Encrypted for String). Answers 400
        /// VALIDATION with Errors naming the problems found: Name (letters and underscore, 4 to 120
        /// characters, not ending in '_Data'), Type (not one of the four names), DecimalPlaces
        /// (missing for a Number, or not 0 to 8), ValidationRegex (not a regular expression),
        /// Minimum (greater than Maximum, or negative for a String), Maximum (below 1 for a
        /// String), Minimum or Maximum (outside -9007199254740991 to 9007199254740991, the whole
        /// numbers a browser handles exactly). The request's shape is checked first (Name letters
        /// and length, Type present),
        /// then the '_Data' suffix and the Type name, then the rules of the type, so a corrected
        /// request can reveal further problems. Answers 409 CONFLICT when the entity takes no new
        /// properties (AllowAddProperties is false) and when it has a property with that name,
        /// whatever its letter case. A refusal of the API server is 400 VALIDATION with its own
        /// message, any other failure of it 502 UPSTREAM_ERROR; in both cases nothing is created.
        /// A 'Warning' response header means the property was created and the API server could
        /// not be refreshed afterwards.
        /// </summary>
        [HttpPost]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(PropertyResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<PropertyResponse>> Create(string appToken, string entity, CreatePropertyRequest request)
        {
            var created = await _propertyManagementService.CreateAsync(appToken, entity, request);

            return Created(LocationOf(appToken, entity, created), created);
        }

        /// <summary>
        /// Changes the description and the rules of a custom property. The type of the stored
        /// property decides what is written: ValidationRegex when AllowValidationRegex, Minimum
        /// when AllowMin, Maximum when AllowMaxEdit; the other values of the request are ignored,
        /// and so is anything else it carries (a type, Required, Encrypted, DecimalPlaces cannot
        /// be changed). A value left out or null is removed. Existing records are not checked
        /// against the new rules. Answers 400 VALIDATION on ValidationRegex (not a regular
        /// expression), on Minimum (greater than the Maximum the property ends up with, or
        /// negative for a String) and on a Minimum or a written Maximum outside
        /// -9007199254740991 to 9007199254740991 (a stored value beyond that range stays until
        /// it is replaced), and 409 CONFLICT for a system property. The API server learns about
        /// the change through its cache reset: a 'Warning' response header means the change was
        /// saved and that reset failed.
        /// </summary>
        [HttpPut("{property}")]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(PropertyResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        public async Task<PropertyResponse> Update(string appToken, string entity, string property, UpdatePropertyRequest request)
        {
            return await _propertyManagementService.UpdateAsync(appToken, entity, property, request);
        }

        /// <summary>
        /// Renames a custom property and its column on the API server. The property's URL changes
        /// with its name. Answers 409 CONFLICT for a system property, for a property that a unique
        /// or foreign key constraint of the entity names (remove the constraint first), and when
        /// another property of the entity has the new name, whatever its letter case. A change of
        /// letter case only (Price to price) is stored in the Portal but the API server keeps the
        /// column as it is; on a PostgreSQL application the property then cannot be read or
        /// written until it is renamed back to the spelling of the column. The default
        /// order, custom endpoints, security rules, reports and history records that name the
        /// property are not changed. A refusal of the API server is 400 VALIDATION with its own
        /// message, any other failure of it 502 UPSTREAM_ERROR; in both cases the name stays.
        /// </summary>
        [HttpPost("{property}/rename")]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(PropertyResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<PropertyResponse> Rename(string appToken, string entity, string property, RenamePropertyRequest request)
        {
            return await _propertyManagementService.RenameAsync(appToken, entity, property, request);
        }

        /// <summary>
        /// Deletes a custom property: its column on the API server with all its values. This
        /// cannot be undone and there is no confirmation step. Answers 409 CONFLICT for a system
        /// property and for a property that a unique or foreign key constraint of the entity
        /// names (remove the constraint first). A refusal of the API server is 400 VALIDATION with
        /// its own message, any other failure of it 502 UPSTREAM_ERROR; in both cases the
        /// property stays.
        /// </summary>
        [HttpDelete("{property}")]
        [ProducesCacheResetWarning]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Delete(string appToken, string entity, string property)
        {
            await _propertyManagementService.DeleteAsync(appToken, entity, property);

            return NoContent();
        }

        private static string LocationOf(string appToken, string entity, PropertyResponse property)
        {
            return $"/api/v1/applications/{Uri.EscapeDataString(appToken)}/entities/{Uri.EscapeDataString(entity)}/properties/{Uri.EscapeDataString(property.Name)}";
        }
    }
}
