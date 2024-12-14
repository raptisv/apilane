using System.ComponentModel.DataAnnotations;

namespace Apilane.Api.Models.ViewModels
{
    public class ApiErrorVm
    {
        /// <summary>
        /// The error code.
        /// </summary>
        [Required]
        public string Code { get; set; } = null!;

        /// <summary>
        /// The error message.
        /// </summary>
        [Required]
        public string Message { get; set; } = null!;

        /// <summary>
        /// The entity involved in the error, if any.
        /// </summary>
        public string? Entity { get; set; }

        /// <summary>
        /// The entity property involved in the error, if any.
        /// </summary>
        public string? Property { get; set; }
    }
}
