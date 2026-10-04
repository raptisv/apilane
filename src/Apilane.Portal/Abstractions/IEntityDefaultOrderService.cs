using Apilane.Portal.Api.V1.Contracts;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The default sorting of an entity of an application the caller owns or collaborates on.
    /// The write follows <see cref="IApplicationWriteScope"/>.
    /// </summary>
    public interface IEntityDefaultOrderService
    {
        /// <summary>
        /// The default sorting of the entity and the properties it can be sorted by.
        /// </summary>
        Task<DefaultOrderResponse> GetAsync(string appToken, string entityName);

        /// <summary>
        /// Replaces the default sorting of the entity; an empty list removes it.
        /// </summary>
        Task<DefaultOrderResponse> ReplaceAsync(string appToken, string entityName, ReplaceDefaultOrderRequest request);
    }
}
