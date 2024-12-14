using Apilane.Common.Attributes;
using Apilane.Common.Enums;
using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Apilane.Common.Models
{
    public class DBWS_ReportItem : DBWS_MainModel
    {        
        public long AppID { get; set; }

        [JsonIgnore]
        public DBWS_Application Application { get; set; } = null!;
        
        public int TypeID { get; set; }

        [JsonIgnore]
        public ReportType TypeID_Enum { get { return (ReportType)TypeID; } }
        
        public int Order { get; set; }
        
        public int PanelWidth { get; set; }

        [AttrRequired]
        [Display(Name = "Title")]
        
        public string Title { get; set; } = null!;

        [AttrRequired]
        
        public string Entity { get; set; } = null!;

        [AttrRequired]
        
        public string Properties { get; set; } = null!;
        
        public string? Filter { get; set; }

        [AttrRequired]
        
        public string GroupBy { get; set; } = null!;

        [AttrRequired]
        [Range(1, 1000)]
        
        public int MaxRecords { get; set; }


        public string GetApiUrl()
        {
            return $"Stats/Aggregate?Entity={Entity}&Properties={Properties}&Filter={Filter}&Sort=Desc&GroupBy={GroupBy}&PageIndex=1&PageSize={MaxRecords}".Replace(" ", "");
        }
    }
}
