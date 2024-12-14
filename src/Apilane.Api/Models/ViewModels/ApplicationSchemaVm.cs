using Apilane.Common.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Apilane.Api.Models.ViewModels
{
    public class ApplicationSchemaVm
    {
        public int AuthTokenExpireMinutes { get; set; }
        public bool AllowLoginUnconfirmedEmail { get; set; }
        public bool ForceSingleLogin { get; set; }
        public bool AllowUserRegister { get; set; }
        public int MaxAllowedFileSizeInKB { get; set; }
        public List<EntitySchemaDto> Entities { get; set; } = null!;

        public class EntitySchemaDto
        {
            public bool ChangeTracking { get; set; }
            public string Name { get; set; } = null!;

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Description { get; set; }
            public bool IsSystem { get; set; }
            public bool IsReadOnly { get; set; }
            public List<PropertySchemaDto> Properties { get; set; } = null!;

            public class PropertySchemaDto
            {
                public bool IsPrimaryKey { get; set; }
                public bool IsSystem { get; set; }
                public string Name { get; set; } = null!;

                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                public string? Description { get; set; }

                public bool Encrypted { get; set; }

                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                public long? Minimum { get; set; }

                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                public long? Maximum { get; set; }

                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                public int? DecimalPlaces { get; set; }
                public bool Required { get; set; }

                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                public string? Type { get; set; }

                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                public string? ValidationRegex { get; set; }
            }
        }

        public static ApplicationSchemaVm GetFromApplication(DBWS_Application application)
        {
            return new ApplicationSchemaVm()
            {
                AuthTokenExpireMinutes = application.AuthTokenExpireMinutes,
                AllowLoginUnconfirmedEmail = application.AllowLoginUnconfirmedEmail,
                ForceSingleLogin = application.ForceSingleLogin,
                AllowUserRegister = application.AllowUserRegister,
                MaxAllowedFileSizeInKB = application.MaxAllowedFileSizeInKB,
                Entities = application.Entities.Select(x => new ApplicationSchemaVm.EntitySchemaDto()
                {
                    Name = x.Name,
                    Description = x.Description,
                    IsSystem = x.IsSystem,
                    IsReadOnly = x.IsReadOnly,
                    ChangeTracking = x.RequireChangeTracking,
                    Properties = x.Properties.Select(p => new ApplicationSchemaVm.EntitySchemaDto.PropertySchemaDto()
                    {
                        IsPrimaryKey = p.IsPrimaryKey,
                        IsSystem = p.IsSystem,
                        Name = p.Name,
                        Description = p.Description,
                        Encrypted = p.Encrypted,
                        Minimum = p.Minimum,
                        Maximum = p.Maximum,
                        DecimalPlaces = p.DecimalPlaces,
                        Required = p.Required,
                        Type = p.TypeID_Enum.ToString(),
                        ValidationRegex = string.IsNullOrWhiteSpace(p.ValidationRegex) ? null : p.ValidationRegex
                    }).ToList()
                }).ToList()
            };
        }
    }
}
