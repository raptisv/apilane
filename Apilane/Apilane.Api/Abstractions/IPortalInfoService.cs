using Apilane.Common.Models;
using System.Threading.Tasks;

namespace Apilane.Api.Abstractions
{
    public interface IPortalInfoService
    {
        Task IsPortalHealhyAsync();
        Task<bool> UserOwnsApplicationAsync(string authToken, string appToken);
        Task<DBWS_Application> GetApplicationAsync(string appToken);
    }
}
