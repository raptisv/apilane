using Apilane.Common.Attributes;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Apilane.Common.Models
{
    public class DBWS_Server : DBWS_MainModel
    {
        [AttrRequired]
        [Display(Name = "Name")]
        public string Name { get; set; } = null!;

        [AttrRequired]
        [Display(Name = "Url")]
        public string ServerUrl { get; set; } = null!;

        [JsonIgnore]
        public List<DBWS_Application> Applications { get; set; } = null!;
    }
}