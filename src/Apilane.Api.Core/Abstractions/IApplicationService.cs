using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Orleans;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Abstractions
{
    public interface IApplicationService : IGrainObserver
    {
        Task ApplicationChangedAsync(string appToken);
        Task<DBWS_Application> GetAsync(string appToken);
        ValueTask<ApplicationDbInfoDto> GetDbInfoAsync(string appToken);
    }
}
