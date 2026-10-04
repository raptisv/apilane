using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// An Apilane API server that hosts applications.
    /// </summary>
    public class ServerResponse
    {
        [Required]
        public long ID { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The server address, as reachable by the Portal and by browsers.
        /// </summary>
        [Required]
        public string ServerUrl { get; set; } = string.Empty;

        /// <summary>
        /// How many applications are hosted on this server.
        /// </summary>
        [Required]
        public int ApplicationCount { get; set; }
    }

    /// <summary>
    /// The name and address of an API server, as every signed-in user may see them.
    /// </summary>
    public class ServerSummaryResponse
    {
        [Required]
        public long ID { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The server address, as reachable by the Portal and by browsers.
        /// </summary>
        [Required]
        public string ServerUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// The values of a server, for creating one and for changing one.
    /// </summary>
    public class ServerRequest
    {
        [Required(ErrorMessage = "Required")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// An absolute http or https URL, e.g. https://api.example.com. Changing it takes effect at once for
        /// every application on the server.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string ServerUrl { get; set; } = string.Empty;
    }
}
