using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/entities")]
    public class EntitiesController : PortalApplicationApiControllerBase
    {
        private readonly IEntityManagementService _entityManagementService;

        public EntitiesController(IEntityManagementService entityManagementService)
        {
            _entityManagementService = entityManagementService;
        }

        /// <summary>
        /// Lists the entities of the application by name, custom and system ones (IsSystem tells
        /// them apart), with their constraints. Their properties are included only with
        /// IncludeProperties=true. For the owner and collaborators. Record counts are not part of
        /// it: they come from the API server (GET {ServerUrl}/api/Stats/CountDataAndHistory?entity=Name).
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ListResponse<EntityResponse>), StatusCodes.Status200OK)]
        public async Task<ListResponse<EntityResponse>> List(string appToken, [FromQuery(Name = "IncludeProperties")] bool includeProperties = false)
        {
            return new ListResponse<EntityResponse>(await _entityManagementService.GetAllAsync(appToken, includeProperties));
        }

        /// <summary>
        /// Returns one entity with its properties. The name is case-sensitive: 404 NOT_FOUND
        /// (Entity) for any other spelling.
        /// </summary>
        [HttpGet("{entity}")]
        [ProducesResponseType(typeof(EntityResponse), StatusCodes.Status200OK)]
        public async Task<EntityResponse> Get(string appToken, string entity)
        {
            return await _entityManagementService.GetAsync(appToken, entity);
        }

        /// <summary>
        /// Creates a custom entity: its table on the API server, with the system properties (ID,
        /// Owner, Created and, when asked for, the differentiation property) and their constraints.
        /// Answers 409 CONFLICT when an entity with that name exists, whatever its letter case, and
        /// 400 VALIDATION on HasDifferentiationProperty when the application has no differentiation
        /// entity. A refusal of the API server is 400 VALIDATION with its own message, any other
        /// failure of it 502 UPSTREAM_ERROR; in both cases nothing is created. A 'Warning' response
        /// header means the entity was created and the API server could not be refreshed afterwards.
        /// </summary>
        [HttpPost]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(EntityResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<EntityResponse>> Create(string appToken, CreateEntityRequest request)
        {
            var created = await _entityManagementService.CreateAsync(appToken, request);

            return Created(LocationOf(appToken, created), created);
        }

        /// <summary>
        /// Changes the description and the change tracking of an entity. Allowed for system
        /// entities too. Answers 400 VALIDATION on RequireChangeTracking when it is true for an
        /// entity whose records cannot be updated (AllowPut is false). The API server learns about
        /// the change through its cache reset: a 'Warning' response header means the change was
        /// saved and that reset failed.
        /// </summary>
        [HttpPut("{entity}")]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(EntityResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<EntityResponse> Update(string appToken, string entity, UpdateEntityRequest request)
        {
            return await _entityManagementService.UpdateAsync(appToken, entity, request);
        }

        /// <summary>
        /// Renames a custom entity and its table on the API server. The entity's URL changes with
        /// its name. Answers 409 CONFLICT for a system entity, when a foreign key of any entity
        /// points to it, and when another entity has the new name, whatever its letter case.
        /// Custom endpoints, security rules and reports that name the entity are not changed. A
        /// refusal of the API server is 400 VALIDATION with its own message, any other failure of
        /// it 502 UPSTREAM_ERROR; in both cases the name stays.
        /// </summary>
        [HttpPost("{entity}/rename")]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(EntityResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<EntityResponse> Rename(string appToken, string entity, RenameEntityRequest request)
        {
            return await _entityManagementService.RenameAsync(appToken, entity, request);
        }

        /// <summary>
        /// Deletes a custom entity: its table on the API server with all its records, and its
        /// properties. This cannot be undone and there is no confirmation step. Answers 409
        /// CONFLICT for a system entity and when a foreign key of any entity points to it. A refusal
        /// of the API server is 400 VALIDATION with its own message, any other failure of it 502
        /// UPSTREAM_ERROR; in both cases the entity stays.
        /// </summary>
        [HttpDelete("{entity}")]
        [ProducesCacheResetWarning]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Delete(string appToken, string entity)
        {
            await _entityManagementService.DeleteAsync(appToken, entity);

            return NoContent();
        }

        private static string LocationOf(string appToken, EntityResponse entity)
        {
            return $"/api/v1/applications/{Uri.EscapeDataString(appToken)}/entities/{Uri.EscapeDataString(entity.Name)}";
        }
    }
}
