using System.IO;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Backups of the Portal database. Callers are responsible for the Admin check.
    /// </summary>
    public interface IBackupService
    {
        /// <summary>
        /// Takes a consistent copy of the Portal database and returns it as a stream. The copy is a
        /// temporary file that is deleted when the stream is disposed. Adds an audit log entry.
        /// </summary>
        Task<Stream> CreateAsync();
    }
}
