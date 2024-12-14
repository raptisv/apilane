using Apilane.Common.Models;

namespace Apilane.Portal.Abstractions
{
    public interface IPortalSettingsService
    {
        GlobalSettings Get();
    }
}
