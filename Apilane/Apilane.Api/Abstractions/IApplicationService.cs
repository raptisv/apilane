using Apilane.Common.Models;
using System.Threading.Tasks;

namespace Apilane.Api.Abstractions
{
    public interface IApplicationService
    {
        Task<DBWS_Application> GetAsync(string appToken);
    }
}
