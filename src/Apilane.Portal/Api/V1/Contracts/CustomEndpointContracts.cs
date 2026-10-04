using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// A custom endpoint of an application: a named SQL query that the API server runs at
    /// GET {ServerUrl}/api/Custom/{Name}. In this API it is identified by its numeric ID.
    /// </summary>
    public class CustomEndpointResponse
    {
        [Required]
        public long ID { get; set; }

        /// <summary>
        /// Letters only. Unique inside the application, whatever its letter case.
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>
        /// The SQL the API server runs. {Name} placeholders are replaced by the numeric values the
        /// caller passes; {Owner} by the ID of the calling user.
        /// </summary>
        [Required]
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// The {Name} placeholders of the query (letters and underscore), each once, in the order
        /// they first appear, without Owner. A caller passes each as a query-string value; a value
        /// that is missing or not a whole number becomes SQL null.
        /// </summary>
        [Required]
        public List<string> Parameters { get; set; } = new List<string>();

        /// <summary>
        /// The address of the endpoint on the API server, with one {Name} placeholder per parameter
        /// and without the application token.
        /// </summary>
        [Required]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// The address a client calls: Url with the application token as the first query-string value.
        /// </summary>
        [Required]
        public string CallUrl { get; set; } = string.Empty;

        /// <summary>
        /// When the endpoint was created through this API (UTC). Endpoints created any other way, such as
        /// the older portal pages or an import, hold 0001-01-01T00:00:00. Editing does not change it.
        /// </summary>
        [Required]
        public DateTime DateModified { get; set; }
    }

    /// <summary>
    /// The values of a custom endpoint, for creating one and for changing one.
    /// </summary>
    public class CustomEndpointRequest
    {
        /// <summary>
        /// Letters only (a-z, A-Z), at most 80. Spaces before and after are removed. Unique inside
        /// the application, whatever its letter case. Renaming changes the address on the API
        /// server, and the security rules written for the old name no longer apply to it.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [RegularExpression(CustomEndpointNameRules.Pattern, ErrorMessage = CustomEndpointNameRules.PatternMessage)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Empty or left out removes the description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// The SQL the API server runs, stored as sent.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Query { get; set; } = string.Empty;
    }

    /// <summary>
    /// A name and a query to compute the parameters and the addresses of, without saving anything.
    /// Nothing is checked: an empty name gives an address that ends in /api/Custom/.
    /// </summary>
    public class CustomEndpointPreviewRequest
    {
        /// <summary>
        /// Spaces before and after are removed, as when saving.
        /// </summary>
        public string? Name { get; set; }

        public string? Query { get; set; }
    }

    /// <summary>
    /// What a custom endpoint with the previewed name and query would have once saved.
    /// </summary>
    public class CustomEndpointPreviewResponse
    {
        /// <summary>
        /// The same as CustomEndpointResponse.Parameters.
        /// </summary>
        [Required]
        public List<string> Parameters { get; set; } = new List<string>();

        /// <summary>
        /// The same as CustomEndpointResponse.Url.
        /// </summary>
        [Required]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// The same as CustomEndpointResponse.CallUrl.
        /// </summary>
        [Required]
        public string CallUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// The rules of a custom endpoint name: the letters of DBWS_CustomEndpoint.Name, with the spaces
    /// around it allowed because they are removed, and at most 80 characters.
    /// </summary>
    public static class CustomEndpointNameRules
    {
        public const string Pattern = @"^\s*[a-zA-Z]{1,80}\s*$";
        public const string PatternMessage = "Letters a-z and A-Z only, at most 80 characters";
    }
}
