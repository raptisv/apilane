using Apilane.Common.Models;
using Apilane.Portal.Models;
using System.Collections.Generic;

namespace Apilane.Portal.Abstractions
{
    public interface ICloneService
    {
        /// <summary>
        /// Starts the clone in the background and returns the id of the operation. The service lives
        /// as long as the Portal and the clone runs after the request has ended, so it cannot read the
        /// Portal database: the caller hands over what the routine needs, the stored installation key
        /// (Instance > Settings) among it. The key is only sent to the target server and is never
        /// kept in the progress of the operation.
        /// </summary>
        string StartCloneAsync(
            DBWS_Application sourceApplication,
            DBWS_Application applicationToClone,
            DBWS_Server targetServer,
            string portalUserAuthToken,
            string installationKey,
            bool cloneData,
            List<string>? entitiesToClone);

        CloneProgressInfo? GetProgress(string operationId);
    }
}
