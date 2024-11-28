using Apilane.Common.Models;
using Orleans;
using System.Threading.Tasks;

namespace Apilane.Api.Abstractions
{
    public interface IApplicationService : IGrainObserver
    {
        Task ApplicationChangedAsync(string appToken);
        Task<DBWS_Application> GetAsync(string appToken);
    }
}
