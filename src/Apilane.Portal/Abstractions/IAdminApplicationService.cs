using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Every application of the instance, whoever owns it. Read only. Callers are responsible for
    /// the Admin check.
    /// </summary>
    public interface IAdminApplicationService
    {
        /// <summary>
        /// Every application, in the order they were created.
        /// </summary>
        Task<List<AdminApplicationResponse>> GetAllAsync();

        /// <summary>
        /// One application with its entities and their properties. The token is matched exactly.
        /// Throws a NOT_FOUND <see cref="Api.PortalException"/> for an unknown token.
        /// </summary>
        Task<AdminApplicationDetailResponse> GetAsync(string appToken);
    }
}
