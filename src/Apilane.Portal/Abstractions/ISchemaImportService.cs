using Apilane.Portal.Api.V1.Contracts;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Adds entities, properties, constraints, security rules and custom endpoints to an
    /// application in one call, and computes what another application has that it lacks. Both for
    /// applications the caller owns or collaborates on.
    /// The import keeps the rules of the older Import schema page and not those of the entity,
    /// property, constraint, security and custom endpoint services: what the application already
    /// has is skipped with a warning or, when it differs, stops the import; nothing else is
    /// checked besides the names and types of what is new. That is why it writes through the
    /// API server client and the database itself instead of calling those services, which would
    /// also reset the API server's cache after every single item.
    /// </summary>
    public interface ISchemaImportService
    {
        /// <summary>
        /// Everything the source application has and this one lacks, as an import request. Names
        /// are matched whatever their letter case. Throws a VALIDATION
        /// <see cref="Api.PortalException"/> when the source is this application and a NOT_FOUND
        /// one when the caller cannot see either of the two.
        /// </summary>
        Task<SchemaImportRequest> GetDiffAsync(string appToken, string sourceAppToken);

        /// <summary>
        /// Applies the request step by step: entities in the foreign-key order of the Razor import
        /// (each with its properties, then its constraints), then security rules, then custom
        /// endpoints, then one reset of the API server's cache. Not atomic: the first step that fails stops the import
        /// with a <see cref="Api.PortalException"/> that names it, the steps before it stay applied
        /// and the cache is not reset. Returns the warnings for what was skipped.
        /// </summary>
        Task<SchemaImportResponse> ImportAsync(string appToken, SchemaImportRequest request);
    }
}
