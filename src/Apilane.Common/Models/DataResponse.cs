using System.Collections.Generic;

namespace Apilane.Common.Models
{
    /// <summary>
    /// The response object
    /// </summary>
    public class DataResponse
    {
        /// <summary>
        /// Array of data records
        /// </summary>
        public List<Dictionary<string, object?>> Data { get; set; } = null!;
    }
}
