namespace Apilane.Net.Models.Data
{
    public class DataTotalResponse<T> : DataResponse<T>
    {
        public int Total { get; set; }
    }
}
