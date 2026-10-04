using Apilane.Common.Models;
using System;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The one order every write to an application follows:
    /// the caller validates and changes the tracked entities, then this calls the API server,
    /// saves the Portal database and tells the API server to reload the application.
    /// </summary>
    public interface IApplicationWriteScope
    {
        /// <summary>
        /// Runs <paramref name="callApiServer"/> (left out for a change that lives in the Portal
        /// only), then saves the tracked changes, then resets the API server's cache and waits for it.
        /// When the API server call fails nothing is saved and the cache is not reset: its
        /// <see cref="Api.PortalException"/> goes to the caller. A failing cache reset does not
        /// fail the write; the response gets the 'Warning' header instead (IApiServerCacheReset).
        /// The application must have its Server loaded.
        /// </summary>
        Task SaveAsync(DBWS_Application application, Func<Task>? callApiServer = null);
    }
}
