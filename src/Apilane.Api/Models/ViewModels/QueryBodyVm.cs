using System.Linq;
using System.Text.Json;

namespace Apilane.Api.Models.ViewModels
{
    /// <summary>
    /// The JSON body of an HTTP QUERY request (RFC 10008) to a read endpoint. It carries the parameters a GET
    /// request carries in its query string, so that a long filter does not hit the length limit of the request
    /// line and needs no URL escaping. The entity and the tokens stay in the query string and the headers.
    /// </summary>
    public abstract class QueryBodyVm
    {
        /// <summary>The filter: a filter object, or that object as a JSON string.</summary>
        public JsonElement? Filter { get; set; }

        /// <summary>
        /// The value of a body property as the text its query string parameter would hold: a JSON string is
        /// taken as it is, an array of strings is joined with commas, an object or another array is its JSON.
        /// </summary>
        public static string? ToParameter(JsonElement? value)
        {
            if (value is null)
            {
                return null;
            }

            var element = value.Value;

            return element.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Array when element.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String)
                    => string.Join(",", element.EnumerateArray().Select(x => x.GetString())),
                _ => element.GetRawText()
            };
        }
    }

    /// <summary>The body of a QUERY request to Data/Get and Files/Get.</summary>
    public class DataQueryBodyVm : QueryBodyVm
    {
        /// <summary>Default value 1. Used for data paging</summary>
        public int PageIndex { get; set; } = 1;

        /// <summary>Default value 20. Range 1-1000, a value outside it is replaced by 1000.</summary>
        public int PageSize { get; set; } = 20;

        /// <summary>The sort: a sort object or an array of them, or that JSON as a string.</summary>
        public JsonElement? Sort { get; set; }

        /// <summary>The properties to fetch: an array of names, or the names comma separated.</summary>
        public JsonElement? Properties { get; set; }

        /// <summary>Default value false. Set it to true to get the count of records according to the filter</summary>
        public bool GetTotal { get; set; }
    }

    /// <summary>The body of a QUERY request to Stats/Aggregate.</summary>
    public class AggregateQueryBodyVm : QueryBodyVm
    {
        /// <summary>Required. The properties with their aggregate (e.g. "ID.Count"): an array, or comma separated.</summary>
        public JsonElement? Properties { get; set; }

        /// <summary>Default value 1. Used for data paging</summary>
        public int PageIndex { get; set; } = 1;

        /// <summary>Default value 20. Range 1-1000, a value outside it is replaced by 1000.</summary>
        public int PageSize { get; set; } = 20;

        /// <summary>Default value NULL. The properties to group by: an array, or comma separated.</summary>
        public JsonElement? GroupBy { get; set; }

        /// <summary>Default value DESC. Accepted values "ASC", "DESC"</summary>
        public string OrderDirection { get; set; } = "DESC";
    }

    /// <summary>The body of a QUERY request to Stats/Distinct.</summary>
    public class DistinctQueryBodyVm : QueryBodyVm
    {
        /// <summary>Required. The property whose distinct values to return</summary>
        public string? Property { get; set; }
    }
}
