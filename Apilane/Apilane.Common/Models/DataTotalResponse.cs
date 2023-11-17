namespace Apilane.Common.Models
{
    /// <summary>
    /// The response object
    /// </summary>
    public class DataTotalResponse : DataResponse
    {
        /// <summary>
        /// The total count of records, according to the specified filter string
        /// </summary>
        public long Total { get; set; }
    }
}
