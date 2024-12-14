using Apilane.Common.Models;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Abstractions
{
    public interface IApplicationNewAPI
    {
        Task<bool> CreateApplicationAsync(DBWS_Application Application);
    }
}
