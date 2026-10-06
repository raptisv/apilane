using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/entities/{entity}/constraints")]
    [AgentPermission("entities")]
    public class EntityConstraintsController : PortalApplicationApiControllerBase
    {
        private readonly IEntityConstraintService _entityConstraintService;

        public EntityConstraintsController(IEntityConstraintService entityConstraintService)
        {
            _entityConstraintService = entityConstraintService;
        }

        /// <summary>
        /// Returns the unique and foreign key constraints of an entity, system ones included
        /// (IsSystem tells them apart), and the names a constraint may use (Candidates). For the
        /// owner and collaborators. Entity names are case-sensitive: 404 NOT_FOUND (Entity) for any
        /// other spelling.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(EntityConstraintsResponse), StatusCodes.Status200OK)]
        public async Task<EntityConstraintsResponse> Get(string appToken, string entity)
        {
            return await _entityConstraintService.GetAsync(appToken, entity);
        }

        /// <summary>
        /// Replaces the custom constraints of an entity with the list sent and applies them to its
        /// table on the API server. System constraints are kept by the server: leave them out. An
        /// empty list removes every custom constraint. Answers 400 VALIDATION with Errors naming
        /// each problem by its place in the list (Constraints[0].Properties, ...): a system
        /// constraint was sent, Type is not Unique or ForeignKey, a unique constraint has no
        /// property or one that does not exist, is encrypted or is listed twice, a foreign key has
        /// a Property, ForeignEntity or OnDelete that is not among the Candidates, or the entity
        /// would have the same constraint twice. Answers 403 FORBIDDEN for a system entity unless
        /// the caller holds the Admin role. A refusal of the API server (existing records break
        /// the constraint, for example) is 400 VALIDATION with its own message, any other failure
        /// of it 502 UPSTREAM_ERROR; in both cases nothing is changed. A 'Warning' response header
        /// means the constraints were saved and the API server could not be refreshed afterwards.
        /// </summary>
        [HttpPut]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(EntityConstraintsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        public async Task<EntityConstraintsResponse> Replace(string appToken, string entity, ReplaceConstraintsRequest request)
        {
            return await _entityConstraintService.ReplaceAsync(appToken, entity, request);
        }
    }
}
