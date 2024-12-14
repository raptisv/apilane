using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Api.Core.Models.AppModules.Files;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Common.Models.AppModules.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Apilane.Api.Core.AppModules
{
    public static class Modules
    {
        public static List<DBWS_Entity> NewApplicationSystemEntities(string differentiationEntity)
        {
            var systemEntities = new List<DBWS_Entity>();

            // Add the differentiation entity first.
            if (!string.IsNullOrWhiteSpace(differentiationEntity))
            {
                systemEntities.Add(new()
                {
                    ID = -1,
                    AppID = -1, // TO FILL
                    IsSystem = true,
                    Name = differentiationEntity,
                    Description = "Differentiation entity",
                    RequireChangeTracking = true,
                    HasDifferentiationProperty = false,
                    EntConstraints = null,
                    Properties = new()
                    {
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = Globals.PrimaryKeyColumn,
                            Required = true,
                            Description = "Primary key",
                            TypeID = (int)PropertyType.Number,
                            DecimalPlaces = 0,
                            IsPrimaryKey = true,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = Globals.CreatedColumn,
                            Required = true,
                            Description = "Created (UTC)",
                            ValidationRegex = null,
                            TypeID = (int)PropertyType.Date,
                            Maximum = int.MaxValue,
                            Minimum = 0,
                            IsPrimaryKey = false,
                        }
                    }
                });
            }

            // Add all system entities next.
            systemEntities.AddRange(new List<DBWS_Entity>()
            {
                new()
                {
                    ID = -1,
                    AppID = -1, // TO FILL
                    IsSystem = true,
                    Name = nameof(Users),
                    Description = "Application users",
                    RequireChangeTracking = true,
                    HasDifferentiationProperty = true,
                    EntConstraints = JsonSerializer.Serialize(new List<EntityConstraint>()
                    {
                        new()
                        {
                            IsSystem = true,
                            TypeID = (int)ConstraintType.Unique,
                            Properties = nameof(Users.Email)
                        },
                        new()
                        {
                            IsSystem = true,
                            TypeID = (int)ConstraintType.Unique,
                            Properties = nameof(Users.Username)
                        }
                    }),
                    Properties = new()
                    {
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(Users.ID),
                            Required = true,
                            Description = "Primary key",
                            TypeID = (int)PropertyType.Number,
                            DecimalPlaces = 0,
                            IsPrimaryKey = true,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(Users.Email),
                            Required = true,
                            Description = "The user email",
                            ValidationRegex = @"[^@]+@[^\.]+\..+", // @"^((([a-z]|\d|[!#\$%&'\*\+\-\/=\?\^_`{\|}~]|[\u00A0-\uD7FF\uF900-\uFDCF\uFDF0-\uFFEF])+(\.([a-z]|\d|[!#\$%&'\*\+\-\/=\?\^_`{\|}~]|[\u00A0-\uD7FF\uF900-\uFDCF\uFDF0-\uFFEF])+)*)|((\x22)((((\x20|\x09)*(\x0d\x0a))?(\x20|\x09)+)?(([\x01-\x08\x0b\x0c\x0e-\x1f\x7f]|\x21|[\x23-\x5b]|[\x5d-\x7e]|[\u00A0-\uD7FF\uF900-\uFDCF\uFDF0-\uFFEF])|(\\([\x01-\x09\x0b\x0c\x0d-\x7f]|[\u00A0-\uD7FF\uF900-\uFDCF\uFDF0-\uFFEF]))))*(((\x20|\x09)*(\x0d\x0a))?(\x20|\x09)+)?(\x22)))@((([a-z]|\d|[\u00A0-\uD7FF\uF900-\uFDCF\uFDF0-\uFFEF])|(([a-z]|\d|[\u00A0-\uD7FF\uF900-\uFDCF\uFDF0-\uFFEF])([a-z]|\d|-|\.|_|~|[\u00A0-\uD7FF\uF900-\uFDCF\uFDF0-\uFFEF])*([a-z]|\d|[\u00A0-\uD7FF\uF900-\uFDCF\uFDF0-\uFFEF])))\.)+(([a-z]|[\u00A0-\uD7FF\uF900-\uFDCF\uFDF0-\uFFEF])|(([a-z]|[\u00A0-\uD7FF\uF900-\uFDCF\uFDF0-\uFFEF])([a-z]|\d|-|\.|_|~|[\u00A0-\uD7FF\uF900-\uFDCF\uFDF0-\uFFEF])*([a-z]|[\u00A0-\uD7FF\uF900-\uFDCF\uFDF0-\uFFEF])))\.?$",
                            TypeID = (int)PropertyType.String,
                            Minimum = 4,
                            Maximum = 100,
                            IsPrimaryKey = false,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(Users.Username),
                            Required = true,
                            Description = "The user name",
                            ValidationRegex = null,
                            TypeID = (int)PropertyType.String,
                            Minimum = 4,
                            Maximum = 100,
                            IsPrimaryKey = false,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = Globals.CreatedColumn,
                            Required = true,
                            Description = "Created (UTC)",
                            ValidationRegex = null,
                            TypeID = (int)PropertyType.Date,
                            Maximum = int.MaxValue,
                            Minimum = 0,
                            IsPrimaryKey = false,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(Users.LastLogin),
                            Required = false,
                            Description = "The date the user was last seen (UTC)",
                            ValidationRegex = null,
                            TypeID = (int)PropertyType.Date,
                            Maximum = int.MaxValue,
                            Minimum = 0,
                            IsPrimaryKey = false,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(Users.EmailConfirmed),
                            Required = true,
                            Description = "True if the user email is verified",
                            TypeID = (int)PropertyType.Boolean,
                            Encrypted = false,
                            Minimum = null,
                            Maximum = null,
                            IsPrimaryKey = false,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(Users.Password),
                            Required = true,
                            Description = "The user password",
                            TypeID = (int)PropertyType.String,
                            Encrypted = true,
                            Minimum = 8,
                            Maximum = 400,
                            IsPrimaryKey = false,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(Users.Roles),
                            Required = false,
                            Description = "The user roles comma separated",
                            TypeID = (int)PropertyType.String,
                            ValidationRegex = "^[a-z\\,]{2,}$",
                            Encrypted = false,
                            Maximum = 1000,
                            IsPrimaryKey = false,
                        }
                    }
                },
                new()
                {
                    ID = -1,
                    AppID = -1, // TO FILL
                    IsSystem = true,
                    IsReadOnly = true,
                    Name = nameof(AuthTokens),
                    Description = "Stores the authentication tokens",
                    RequireChangeTracking = false,
                    HasDifferentiationProperty = false,
                    EntConstraints = JsonSerializer.Serialize(new List<EntityConstraint>()
                    {
                        new()
                        {
                            IsSystem = true,
                            TypeID = (int)ConstraintType.Unique,
                            Properties = nameof(AuthTokens.Token)
                        },
                        new()
                        {
                            IsSystem = true,
                            TypeID = (int)ConstraintType.ForeignKey,
                            Properties = $"{nameof(AuthTokens.Owner)},{nameof(Users)},{ForeignKeyLogic.ON_DELETE_CASCADE}"
                        }
                    }),
                    Properties = new()
                    {
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(AuthTokens.ID),
                            Description = "Primary key",
                            Required = true,
                            TypeID = (int)PropertyType.Number,
                            IsPrimaryKey = true,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(AuthTokens.Token),
                            Required = true,
                            Description = "The authentication token (auto generated on login)",
                            TypeID = (int)PropertyType.String,
                            Maximum = 100,
                            IsPrimaryKey = false,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = Globals.OwnerColumn,
                            Required = true,
                            Description = "The owner",
                            TypeID = (int)PropertyType.Number,
                            DecimalPlaces = 0,
                            IsPrimaryKey = false,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = Globals.CreatedColumn,
                            Required = true,
                            Description = "Created (UTC)",
                            ValidationRegex = null,
                            TypeID = (int)PropertyType.Date,
                            Maximum = int.MaxValue,
                            Minimum = 0,
                            IsPrimaryKey = false,
                        }
                    }
                },
                new()
                {
                    ID = -1,
                    AppID = -1, // TO FILL
                    IsSystem = true,
                    Name = nameof(Files),
                    Description = "Stores the files",
                    RequireChangeTracking = false,
                    HasDifferentiationProperty = false,
                    EntConstraints = JsonSerializer.Serialize(new List<EntityConstraint>()
                    {
                        new()
                        {
                            IsSystem = true,
                            TypeID = (int)ConstraintType.Unique,
                            Properties = nameof(Files.UID)
                        },
                        new()
                        {
                            IsSystem = true,
                            TypeID = (int)ConstraintType.ForeignKey,
                            Properties = $"{nameof(Files.Owner)},{nameof(Users)},{ForeignKeyLogic.ON_DELETE_NO_ACTION}"
                        }
                    }),
                    Properties = new()
                    {
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(Files.ID),
                            Required = true,
                            Description = "The primary key",
                            TypeID = (int)PropertyType.Number,
                            IsPrimaryKey = true,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(Files.Name),
                            Required = true,
                            Description = "The file name",
                            TypeID = (int)PropertyType.String,
                            IsPrimaryKey = false,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(Files.UID),
                            Required = true,
                            Description = "The file's unique identifier",
                            TypeID = (int)PropertyType.String,
                            IsPrimaryKey = false,
                            Maximum = 400,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(Files.Size),
                            Required = true,
                            Description = "The file's size in MB",
                            TypeID = (int)PropertyType.Number,
                            DecimalPlaces = 6,
                            IsPrimaryKey = false,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = nameof(Files.Public),
                            Required = true,
                            Description = "True if the file is public",
                            TypeID = (int)PropertyType.Boolean,
                            DecimalPlaces = null,
                            IsPrimaryKey = false,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = Globals.OwnerColumn,
                            Required = false,
                            Description = "The file owner",
                            TypeID = (int)PropertyType.Number,
                            DecimalPlaces = 0,
                            IsPrimaryKey = false,
                        },
                        new()
                        {
                            ID = -1,
                            IsSystem = true,
                            EntityID = -1,// TO FILL
                            Name = Globals.CreatedColumn,
                            Required = true,
                            Description = "The date the file was uploaded (UTC)",
                            ValidationRegex = null,
                            TypeID = (int)PropertyType.Date,
                            Maximum = int.MaxValue,
                            Minimum = 0,
                            IsPrimaryKey = false,
                        }
                    }
                }
            });

            // Update any required properties accordingly.
            if (!string.IsNullOrWhiteSpace(differentiationEntity))
            {
                // Add module specific property on the required entities.
                foreach (var entity in systemEntities.Where(e => e.HasDifferentiationProperty))
                {
                    var constraints = string.IsNullOrWhiteSpace(entity.EntConstraints) 
                        ? new List<EntityConstraint>()
                        : JsonSerializer.Deserialize<List<EntityConstraint>>(entity.EntConstraints)!;

                    constraints.Add(new EntityConstraint()
                    {
                        IsSystem = true,
                        TypeID = (int)ConstraintType.ForeignKey,
                        Properties = $"{differentiationEntity.GetDifferentiationPropertyName()},{differentiationEntity},{ForeignKeyLogic.ON_DELETE_NO_ACTION}"
                    });

                    entity.EntConstraints = JsonSerializer.Serialize(constraints);

                    entity.Properties.Add(new DBWS_EntityProperty()
                    {
                        IsSystem = true,
                        ID = -1,
                        Description = "Differentiation property",
                        IsPrimaryKey = false,
                        Name = differentiationEntity.GetDifferentiationPropertyName(),
                        EntityID = entity.ID,
                        Maximum = int.MaxValue,
                        Minimum = 0,
                        Required = false,
                        DateModified = DateTime.UtcNow,
                        TypeID = (int)PropertyType.Number,
                        DecimalPlaces = 0
                    });
                }
            }

            return systemEntities;
        }

        public static (List<DBWS_EntityProperty> Properties, List<EntityConstraint> Constraints) NewEntityPropertiesConstraints(
            string? differentiationEntity,
            bool entityHasDifferentiationProperty)
        {
            var properties = new List<DBWS_EntityProperty>();

            properties = new()
            {
                new()
                {
                    IsSystem = true,
                    ID = -1,
                    Description = "Primary key",
                    IsPrimaryKey = true,
                    Name = "ID",
                    EntityID = -1, // TO FILL
                    Maximum = null,
                    Minimum = null,
                    Required = true,
                    DateModified = DateTime.UtcNow,
                    TypeID = (int)PropertyType.Number,
                    DecimalPlaces = 0,
                },
                new()
                {
                    IsSystem = true,
                    ID = -1,
                    Description = "The record owner",
                    IsPrimaryKey = false,
                    Name = Globals.OwnerColumn,
                    EntityID = -1, // TO FILL
                    Maximum = null,
                    Minimum = null,
                    Required = false,
                    DateModified = DateTime.UtcNow,
                    TypeID = (int)PropertyType.Number,
                    DecimalPlaces = 0
                },
                new()
                {
                    IsSystem = true,
                    ID = -1,
                    Description = "Date created (UTC)",
                    IsPrimaryKey = false,
                    Name = Globals.CreatedColumn,
                    EntityID = -1, // TO FILL
                    Maximum = int.MaxValue,
                    Minimum = 0,
                    Required = true,
                    DateModified = DateTime.UtcNow,
                    TypeID = (int)PropertyType.Date
                }
            };

            var entityShouldHaveDifferentiationProperty = !string.IsNullOrWhiteSpace(differentiationEntity) && entityHasDifferentiationProperty;

            // If there is a differentiation property
            if (entityShouldHaveDifferentiationProperty &&
                !string.IsNullOrWhiteSpace(differentiationEntity))
            {
                properties.Add(new DBWS_EntityProperty()
                {
                    IsSystem = true,
                    ID = -1,
                    Description = "Differentiation property",
                    IsPrimaryKey = false,
                    Name = differentiationEntity.GetDifferentiationPropertyName(),
                    EntityID = -1, // TO FILL
                    Maximum = int.MaxValue,
                    Minimum = 0,
                    Required = false,
                    DateModified = DateTime.UtcNow,
                    TypeID = (int)PropertyType.Number,
                    DecimalPlaces = 0
                });
            }

            // Add the constraints
            var constraints = new List<EntityConstraint>()
            {
                new()
                {
                    IsSystem = true,
                    TypeID = (int)ConstraintType.ForeignKey,
                    Properties = $"{Globals.OwnerColumn},{nameof(Users)},{ForeignKeyLogic.ON_DELETE_NO_ACTION}"
                }
            };

            if (entityShouldHaveDifferentiationProperty &&
                !string.IsNullOrWhiteSpace(differentiationEntity))
            {
                constraints.Add(new EntityConstraint()
                {
                    IsSystem = true,
                    TypeID = (int)ConstraintType.ForeignKey,
                    Properties = $"{differentiationEntity.GetDifferentiationPropertyName()},{differentiationEntity},{ForeignKeyLogic.ON_DELETE_NO_ACTION}"
                });
            }

            return (properties, constraints);
        }
    }
}
