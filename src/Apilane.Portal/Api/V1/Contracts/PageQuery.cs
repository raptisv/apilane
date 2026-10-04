using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// Which page of a long list to return. A value out of range is answered with 400 VALIDATION.
    /// </summary>
    public class PageQuery
    {
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;

        /// <summary>
        /// The page number, starting at 1.
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "Must be 1 or more")]
        [DefaultValue(1)]
        public int Page { get; set; } = 1;

        /// <summary>
        /// How many items a page holds, 1 to 200.
        /// </summary>
        [Range(1, MaxPageSize, ErrorMessage = "Must be between 1 and 200")]
        [DefaultValue(DefaultPageSize)]
        public int PageSize { get; set; } = DefaultPageSize;
    }
}
