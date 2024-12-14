using Apilane.Common.Attributes;
using System.Text.Json.Serialization;

namespace Apilane.Common.Models
{
    // IMPORTANT. Do not add virtual properties in this class

    public class DBWS_Collaborate : DBWS_MainModel
    {
        
        public long AppID { get; set; }

        [JsonIgnore]
        public DBWS_Application Application { get; set; } = null!;

        [AttrRequired]
        
        public string UserEmail { get; set; } = null!;
    }
}
