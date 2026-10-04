using Apilane.Common.Attributes;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Apilane.Common.Models
{
    public class DBWS_EntityProperty : DBWS_MainModel, ICloneable
    {
        [AttrRequired]
        [Display(Name = "Entity")]
        public long EntityID { get; set; }

        [JsonIgnore]
        public DBWS_Entity? Entity { get; set; }

        [AttrRequired]
        public bool IsPrimaryKey { get; set; }

        public bool IsSystem { get; set; }

        public bool Encrypted { get; set; }

        public string? ValidationRegex { get; set; }

        public int? DecimalPlaces { get; set; }

        [AttrRequired]
        [Display(Name = "Name")]
        [RegularExpression(@"^[a-zA-Z_]+$.*(?<!_Data)$", ErrorMessage = "Allowed chatracters are a-z, A-Z and _")]
        [MinLength(4), MaxLength(120)]

        public string Name { get; set; } = null!;

        [Display(Name = "Description")]

        public string? Description { get; set; }

        [AttrRequired]
        [Display(Name = "Type")]
        [Range(1, int.MaxValue)]

        public int TypeID { get; set; }

        [JsonIgnore]
        public PropertyType TypeID_Enum
        {
            get
            {
                return (PropertyType)TypeID;
            }
        }

        [AttrRequired]
        [Display(Name = "Required")]
        public bool Required { get; set; }

        [Display(Name = "Min")]
        [Range(long.MinValue, long.MaxValue)]
        public long? Minimum { get; set; }

        [Display(Name = "Max")]
        [Range(long.MinValue, long.MaxValue)]
        public long? Maximum { get; set; }

        public bool IsOnUTC()
        {
            return TypeID_Enum == PropertyType.Date
                &&
                IsSystem
                &&
                (Name.Equals("LastLogin") || Name.Equals(Globals.CreatedColumn));
        }

        public bool AllowEdit(
            string? differentiationEntity,
            bool entityHasDifferentiationProperty)
        {
            if (IsPrimaryKey)
            {
                return false;
            }

            if (IsSystem
                &&
                (
                    Name.Equals(Globals.OwnerColumn)
                    ||
                    Name.Equals(Globals.CreatedColumn)
                    ||
                    Name.Equals("EmailConfirmed")
                    ||
                    Name.Equals("LastLogin")
                ))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(differentiationEntity) &&
                entityHasDifferentiationProperty &&
                Name.Equals(differentiationEntity.GetDifferentiationPropertyName()))
            {
                return false;
            }

            return true;
        }

        public bool AllowMin()
        {
            return !IsPrimaryKey && (TypeID_Enum == PropertyType.Number || TypeID_Enum == PropertyType.String);
        }

        public bool AllowMax()
        {
            return !IsPrimaryKey && (TypeID_Enum == PropertyType.Number || TypeID_Enum == PropertyType.String);
        }

        public bool AllowMaxEdit()
        {
            return !IsPrimaryKey && TypeID_Enum == PropertyType.Number;
        }

        public bool AllowDecimalPlaces()
        {
            return TypeID_Enum == PropertyType.Number;
        }

        public bool AllowValidationRegex()
        {
            return TypeID_Enum == PropertyType.String;
        }

        public bool AllowEncrypted()
        {
            return TypeID_Enum == PropertyType.String;
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
