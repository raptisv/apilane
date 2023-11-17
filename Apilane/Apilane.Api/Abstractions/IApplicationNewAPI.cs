using Apilane.Common.Models;
using System.Threading.Tasks;

namespace Apilane.Api.Abstractions
{
    public interface IApplicationNewAPI
    {
        Task<bool> CreateApplicationAsync(DBWS_Application Application);
    }
}
