using Apilane.Common.Attributes;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Apilane.Common.Models
{

    public class DBWS_CustomEndpoint : DBWS_MainModel
    {
        
        public long AppID { get; set; }

        [JsonIgnore]
        public DBWS_Application Application { get; set; } = null!;

        [AttrRequired]
        [RegularExpression(@"^[a-zA-Z]+$", ErrorMessage = "Allowed chatracters are a-z, A-Z")]
        
        public string Name { get; set; } = null!;

        
        public string? Description { get; set; }

        [AttrRequired]
        
        public string Query { get; set; } = null!;

        public List<string> GetParameters()
        {
            List<string> result = new List<string>();

            var pattern = @"\{([a-zA-Z_]+?)\}";
            var matches = Regex.Matches(Utils.GetString(Query), pattern);

            foreach (Match m in matches)
            {
                string temp = m.Value.Trim('{').Trim('}');
                if (!string.IsNullOrWhiteSpace(temp))
                {
                    result.Add(temp);
                }
            }

            result = result.Distinct().ToList();

            // Always remove Owner
            result.Remove("Owner");

            return result;
        }

        public string GetUrl(string serverUrl, string appToken, bool appendAppToken)
        {
            string url = $"{serverUrl.Trim('/')}/api/Custom/{Name}";

            var parameters = GetParameters();

            var allParams = new Dictionary<string, string>();

            if (appendAppToken)
            {
                allParams.Add(Globals.ApplicationTokenQueryParam, appToken);
            }

            foreach(var parameter in parameters)
            {
                allParams.Add(parameter, $"{{{parameter}}}");
            }

            if (allParams.Count > 0)
            {
                url += $"?{string.Join("&", allParams.Select(x => $"{x.Key}={x.Value}"))}";
            }

            return url;
        }
    }
}
