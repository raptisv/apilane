using Apilane.Portal.Api.V1.Contracts;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Reads the audit log. Callers are responsible for the access check.
    /// </summary>
    public interface IAuditLogService
    {
        /// <summary>
        /// One page of the audit log, newest first: the entries of one application, or the
        /// instance-level entries (servers, settings, user roles) when <paramref name="appId"/> is null.
        /// </summary>
        Task<ListResponse<AuditLogEntryResponse>> QueryAsync(long? appId, PageQuery page);
    }
}
