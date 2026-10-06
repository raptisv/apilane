using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/entities/{entity}/default-order")]
    [AgentPermission("entities")]
    public class EntityDefaultOrderController : PortalApplicationApiControllerBase
    {
        private readonly IEntityDefaultOrderService _entityDefaultOrderService;

        public EntityDefaultOrderController(IEntityDefaultOrderService entityDefaultOrderService)
        {
            _entityDefaultOrderService = entityDefaultOrderService;
        }

        /// <summary>
        /// Returns the default sorting of an entity (the order its records come in when a request
        /// asks for none) and the properties it can be sorted by (Candidates). For the owner and
        /// collaborators. A stored property that no longer exists is left out. Entity names are
        /// case-sensitive: 404 NOT_FOUND (Entity) for any other spelling.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(DefaultOrderResponse), StatusCodes.Status200OK)]
        public async Task<DefaultOrderResponse> Get(string appToken, string entity)
        {
            return await _entityDefaultOrderService.GetAsync(appToken, entity);
        }

        /// <summary>
        /// Replaces the default sorting of an entity with the list sent; an empty list removes it.
        /// Allowed for system entities too. Answers 400 VALIDATION with Errors naming each problem
        /// by its place in the list (Items[0].Property, ...): a property that does not exist or is
        /// listed twice, or a Direction that is not asc or desc. The API server learns about the
        /// change through its cache reset: a 'Warning' response header means the change was saved
        /// and that reset failed.
        /// </summary>
        [HttpPut]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(DefaultOrderResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<DefaultOrderResponse> Replace(string appToken, string entity, ReplaceDefaultOrderRequest request)
        {
            return await _entityDefaultOrderService.ReplaceAsync(appToken, entity, request);
        }
    }
}
