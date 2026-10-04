using Apilane.Common.Models;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The cache reset every write to an application ends with. Call it after the change has been
    /// saved, never before and never when the write failed.
    /// </summary>
    public interface IApiServerCacheReset
    {
        /// <summary>
        /// Makes the API server reload the application and waits for it. When that fails the write
        /// still stands: the failure is logged and the response gets the 'Warning' header, so the
        /// caller can tell the user. The application must have its Server loaded.
        /// </summary>
        Task ResetAfterWriteAsync(DBWS_Application application);
    }
}
