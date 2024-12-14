using System.Threading.Tasks;

namespace Apilane.Net.Abstractions
{
    public interface IApilaneAuthTokenProvider
    {
        Task<string?> GetAuthTokenAsync();
    }
}
