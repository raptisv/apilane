using Apilane.Portal.Api.V1.Contracts;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The unique and foreign key constraints of an entity of an application the caller owns or
    /// collaborates on. The write follows <see cref="IApplicationWriteScope"/>.
    /// </summary>
    public interface IEntityConstraintService
    {
        /// <summary>
        /// The constraints of the entity and the names a new constraint may use.
        /// </summary>
        Task<EntityConstraintsResponse> GetAsync(string appToken, string entityName);

        /// <summary>
        /// Replaces the custom constraints of the entity; its system constraints are kept as they
        /// are stored. For a system entity the caller must hold the Admin role.
        /// </summary>
        Task<EntityConstraintsResponse> ReplaceAsync(string appToken, string entityName, ReplaceConstraintsRequest request);
    }
}
