using Apilane.Common.Models;
using Apilane.Portal.Models;
using System.Collections.Generic;

namespace Apilane.Portal.Abstractions
{
    public interface ICloneService
    {
        string StartCloneAsync(
            DBWS_Application sourceApplication,
            DBWS_Application applicationToClone,
            DBWS_Server targetServer,
            string portalUserAuthToken,
            bool cloneData,
            List<string>? entitiesToClone);

        CloneProgressInfo? GetProgress(string operationId);
    }
}
