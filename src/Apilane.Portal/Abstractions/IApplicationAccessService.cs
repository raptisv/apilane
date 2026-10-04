using Apilane.Common.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Decides which applications the signed-in user may work with: the ones they own and the ones
    /// shared with their e-mail address. The Admin role gives no access here.
    /// </summary>
    public interface IApplicationAccessService
    {
        /// <summary>
        /// The caller's applications with their server, collaborators and custom endpoints, owned first, then by name. Not tracked.
        /// </summary>
        Task<List<DBWS_Application>> GetVisibleApplicationsAsync();

        /// <summary>
        /// One of the caller's applications, with its server, collaborators and custom endpoints. Throws a NOT_FOUND
        /// <see cref="Api.PortalException"/> for a token that is unknown or belongs to an application the
        /// caller cannot see, and a FORBIDDEN one when <paramref name="requireOwner"/> is set and the
        /// caller is a collaborator.
        /// </summary>
        Task<DBWS_Application> GetApplicationAsync(string appToken, bool requireOwner = false);

        /// <summary>
        /// Like <see cref="GetApplicationAsync"/>, except that a caller with the Admin role gets any
        /// application of the instance. Only for cache-reset, the one action an administrator may
        /// run on applications of other users.
        /// </summary>
        Task<DBWS_Application> GetApplicationOrAnyForAdminAsync(string appToken);

        /// <summary>
        /// Like <see cref="GetApplicationAsync"/>, with the application's entities and their
        /// properties loaded too. Tracked, so changes to them are saved and audited.
        /// </summary>
        Task<DBWS_Application> GetApplicationWithEntitiesAsync(string appToken);

        /// <summary>
        /// The entity with exactly this name (case-sensitive). Throws a NOT_FOUND
        /// <see cref="Api.PortalException"/> when the application has none. The application must
        /// come from <see cref="GetApplicationWithEntitiesAsync"/>.
        /// </summary>
        DBWS_Entity GetEntity(DBWS_Application application, string entityName);

        /// <summary>
        /// The property with exactly this name (case-sensitive). Throws a NOT_FOUND
        /// <see cref="Api.PortalException"/> when the entity has none.
        /// </summary>
        DBWS_EntityProperty GetProperty(DBWS_Entity entity, string propertyName);
    }
}
