using System.Collections.Generic;

namespace Apilane.Net.Models.Data
{
    public class DataResponse<T>
    {
        public List<T> Data { get; set; } = null!;
    }
}
