using System.Threading.Tasks;

namespace Apilane.Data.Abstractions
{
    public interface IApplicationDataStoreFactory : IDataStorageRepository
    {
        Task<IDataStorageRepository> CurrentDataStoreAsync();
    }
}
