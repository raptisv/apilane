using Apilane.Common.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apilane.Common.Models
{
    public class DBWS_Entity : DBWS_MainModel
    {        
        public long AppID { get; set; }

        [JsonIgnore]
        public DBWS_Application Application { get; set; } = null!;

        [AttrRequired]
        [Display(Name = "Name")]
        [RegularExpression(@"^[a-zA-Z_]+$", ErrorMessage = "Allowed chatracters are a-z, A-Z and _")]
        [MinLength(4), MaxLength(30)]        
        public string Name { get; set; } = null!;

        [Display(Name = "Description")]        
        public string? Description { get; set; }

        [Display(Name = "Record change tracking")]        
        public bool RequireChangeTracking { get; set; }

        [Display(Name = "Is read only")]        
        public bool IsReadOnly { get; set; }

        [Display(Name = "Is system")]        
        public bool IsSystem { get; set; }

        [Display(Name = "Has differentiation property")]
        public bool HasDifferentiationProperty { get; set; }        

        [Display(Name = "Maximum allowed records per user")]
        [Range(1, long.MaxValue)]
        
        public virtual List<DBWS_EntityProperty> Properties { get; set; } = null!;

        [Display(Name = "EntConstraints")]
        
        public string? EntConstraints { get; set; }

        [JsonIgnore]
        public List<EntityConstraint> Constraints
        {
            get
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(EntConstraints))
                    {
                        return JsonSerializer.Deserialize<List<EntityConstraint>>(EntConstraints)!;
                    }
                }
                catch (Exception ex) // Probably a migration issue
                {
                    Console.WriteLine($"Error deserializing EntConstraints | Probably migration issue | Current value is '{EntConstraints}' | Error {ex.Message}");
                    
                }

                return new List<EntityConstraint>();
            }
        }

        [Display(Name = "EntDefaultOrder")]
        
        public string? EntDefaultOrder { get; set; }

        [JsonIgnore]
        public IEnumerable<SortData> DefaultOrder
        {
            get
            {
                return SortData.ParseList(EntDefaultOrder) ?? Enumerable.Empty<SortData>();
            }
        }

        public bool AllowAddProperties()
        {
            return
                !IsReadOnly
                &&
                !Name.Equals("Files");
        }

        public bool HasOwnerColumn()
        {
            return Properties.Any(x => x.IsSystem && x.Name.Equals(Globals.OwnerColumn));
        }

        public bool AllowPost()
        {
            return
                !IsReadOnly
                &&
                !Name.Equals("Users");
        }

        public bool AllowPut()
        {
            return
                !IsReadOnly
                &&
                !Name.Equals("Files");
        }

        public bool AllowDelete()
        {
            return !IsReadOnly;
        }
    }
}
