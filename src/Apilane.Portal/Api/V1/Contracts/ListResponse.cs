using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// The shape of every list answer, the same as the data API: the items and their total count.
    /// </summary>
    public class ListResponse<T>
    {
        [Required]
        public List<T> Data { get; set; } = new List<T>();

        [Required]
        public long Total { get; set; }

        public ListResponse()
        {
        }

        public ListResponse(List<T> data)
        {
            Data = data;
            Total = data.Count;
        }

        /// <summary>
        /// One page of a longer list: <paramref name="total"/> counts every item, not only the page.
        /// </summary>
        public ListResponse(List<T> data, long total)
        {
            Data = data;
            Total = total;
        }
    }
}
