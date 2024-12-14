using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static Apilane.Common.Models.AggregateData;
using static Apilane.Portal.Controllers.ReportsController;

namespace Apilane.Portal.Controllers
{
    [Authorize]
    public class EntityController : BaseWebApplicationEntityController
    {
        public EntityController(
            ApplicationDbContext dbContext,
            IApiHttpService apiHttpService)
            : base(dbContext, apiHttpService)
        {

        }

        public IActionResult Properties()
        {
            return View(Entity.Properties);
        }

        [HttpGet]
        public ActionResult PropertyCreate()
        {
            if (!Entity.AllowAddProperties())
            {
                return RedirectToIndex();
            }

            return View();
        }

        private void ValidateProperty(ref DBWS_EntityProperty model)
        {
            if (!model.AllowMin())
            {
                model.Minimum = null;
            }

            if (!model.AllowMax())
            {
                model.Maximum = null;
            }

            if (!model.AllowDecimalPlaces())
            {
                model.DecimalPlaces = null;
            }

            if (model.TypeID_Enum == PropertyType.Number &&
                model.DecimalPlaces is null)
            {
                ModelState.AddModelError(nameof(DBWS_EntityProperty.DecimalPlaces), "Required");
            }

            if (!model.AllowValidationRegex())
            {
                model.ValidationRegex = null;
            }

            if (!model.AllowEncrypted())
            {
                model.Encrypted = false;
            }

            if (!string.IsNullOrWhiteSpace(model.ValidationRegex) &&
                !Utils.IsValidRegex(model.ValidationRegex))
            {
                ModelState.AddModelError(nameof(DBWS_EntityProperty.ValidationRegex), "Invalid regex");
            }

            if (model.Minimum.HasValue &&
                model.Maximum.HasValue &&
                model.Minimum.Value > model.Maximum.Value)
            {
                ModelState.AddModelError(nameof(DBWS_EntityProperty.Minimum), "Min cannot be greater than Max");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PropertyCreate(DBWS_EntityProperty model)
        {
            if (!Entity.AllowAddProperties())
            {
                return RedirectToIndex();
            }

            model.Name = Utils.GetString(model.Name);

            var sameName = DBContext.EntityProperties.Where(x => x.EntityID == Entity.ID && x.Name.ToLower() == model.Name.ToLower()).ToList();

            if (sameName.Any())
            {
                ModelState.AddModelError(nameof(DBWS_EntityProperty.Name), $"Property name '{model.Name}' already exists");
            }

            ValidateProperty(ref model);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                model.EntityID = Entity.ID;
                model.IsSystem = false;

                DBContext.EntityProperties.Add(model);

                var apiResponse = await ApiHttpService.PostAsync($"{Application.Server.ServerUrl}/api/Application/GenerateProperty?Entity={Entity.Name}", Application.Token, PortalUserAuthToken, model);

                apiResponse.Match(
                    jsonString => "OK",
                    errorHttpStatusCode =>
                    {
                        throw new Exception($"Could not get a response from Api server | Error code {errorHttpStatusCode}");
                    }
                );

                await DBContext.SaveChangesAsync();

                return RedirectToRoute("EntRoute", new { appid = Application.Token, entid = Entity.Name, controller = "Entity", action = "Properties" });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(model);
            }
        }

        private ActionResult RedirectToIndex()
        {
            return RedirectToRoute("AppRoute", new { appid = Application.Token, controller = "Application", action = "Entities" });
        }        

        [HttpGet]
        public ActionResult Edit()
        {
            return View(Entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(DBWS_Entity model)
        {
            List<string> updateValues = new List<string>() {
                nameof(DBWS_Entity.Description),
                nameof(DBWS_Entity.RequireChangeTracking)
            };

            // Remove all keys, allow only needed
            foreach (var key in ModelState.Keys)
            {
                if (!updateValues.Contains(key))
                    ModelState.Remove(key);
            }

            if (!ModelState.IsValid)
                return View(Entity);

            try
            {
                DBContext.Entry(Entity).State = EntityState.Detached;

                DBContext.Attach(model);
                DBContext.Entry(model).Property(x => x.Description).IsModified = true;
                DBContext.Entry(model).Property(x => x.RequireChangeTracking).IsModified = true;
                await DBContext.SaveChangesAsync();

                return RedirectToIndex();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(Entity);
            }
        }

        [HttpGet]
        public ActionResult Constraints()
        {
            if (Entity.IsSystem)
            {
                // Allow edit system entites only for admin
                if (!(User?.IsInRole(Globals.AdminRoleName) ?? false))
                {
                    return RedirectToIndex();
                }
            }

            var model = new List<EntityConstraint>(Entity.Constraints);

            // Add 1 empty row for each new constraint that needs to be created
            model.Add(new EntityConstraint()
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.Unique,
                Properties = null
            });

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Constraints(List<EntityConstraint> model)
        {
            if (Entity.IsSystem)
            {
                // Allow edit system entites only for admin
                if (!(User?.IsInRole(Globals.AdminRoleName) ?? false))
                {
                    return RedirectToIndex();
                }
            }

            List<string> updateValues = new List<string>() {
                nameof(DBWS_Entity.EntConstraints)
            };

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var validConstraints = model.Where(x => !string.IsNullOrWhiteSpace(x.Properties)).DistinctBy(x => x.Properties);
                Entity.EntConstraints = JsonSerializer.Serialize(validConstraints);

                DBContext.Attach(Entity);
                updateValues.ForEach(x => DBContext.Entry(Entity).Property(x).IsModified = true);

                var apiResponse = await ApiHttpService.PostAsync($"{Application.Server.ServerUrl}/api/Application/GenerateConstraints?Entity={Entity.Name}", Application.Token, PortalUserAuthToken, validConstraints);

                apiResponse.Match(
                    jsonString => "OK",
                    errorHttpStatusCode =>
                    {
                        throw new Exception($"Could not get a response from Api server | Error code {errorHttpStatusCode}");
                    }
                );

                await DBContext.SaveChangesAsync();

                return RedirectToRoute("EntRoute", new { controller = "Entity", action = "Constraints", appid = Application.Token, entid = Entity.Name });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public ActionResult DefaultOrder()
        {
            return View(Entity.DefaultOrder);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DefaultOrderSave()
        {
            List<string> updateValues = new List<string>() {
                nameof(DBWS_Entity.EntDefaultOrder)
            };

            try
            {
                var sortList = SortData.ParseList(Request.Form["DefaultOrder"]);
                Entity.EntDefaultOrder =  JsonSerializer.Serialize(sortList);

                DBContext.Attach(Entity);
                DBContext.Entry(Entity).Property(x => x.EntDefaultOrder).IsModified = true;
                await DBContext.SaveChangesAsync();

                return RedirectToIndex();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View("DefaultOrder", Entity.DefaultOrder);
            }
        }


        [HttpGet]
        public ActionResult Rename()
        {
            if (Entity.IsSystem)
                return RedirectToIndex();

            return View(Entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rename(DBWS_Entity model)
        {
            if (Entity.IsSystem)
            {
                return RedirectToIndex();
            }

            var updateValues = new List<string>() {
                nameof(DBWS_Entity.Name)
            };

            // Remove all keys, allow only needed
            foreach (var key in ModelState.Keys)
            {
                if (!updateValues.Contains(key))
                {
                    ModelState.Remove(key);
                }
            }

            // Before changing the entity name, check FK properties of other Entities
            foreach (var Ent2 in Application.Entities)
            {
                var fkEntity = Ent2.Constraints.FirstOrDefault(c => c.TypeID == (int)ConstraintType.ForeignKey && (c.GetForeignKeyProperties().FKEntity ?? string.Empty).Equals(Entity.Name));
                if (fkEntity is not null)
                {
                    ModelState.AddModelError("CustomError", $"Cannot rename entity as it is referenced by '{Ent2.Name}'");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(Entity);
            }

            try
            {
                // Change the entity name
                Entity.Name = model.Name;

                DBContext.Attach(Entity);
                DBContext.Entry(Entity).Property(x => x.Name).IsModified = true;

                var apiResponse = await ApiHttpService.GetAsync($"{Application.Server.ServerUrl}/api/Application/RenameEntity?ID={model.ID}&NewName={model.Name}", Application.Token, PortalUserAuthToken);

                apiResponse.Match(
                    jsonString => "OK",
                    errorHttpStatusCode =>
                    {
                        throw new Exception($"Could not get a response from Api server | Error code {errorHttpStatusCode}");
                    }
                );

                await DBContext.SaveChangesAsync();

                return RedirectToIndex();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(Entity);
            }
        }

        [HttpGet]
        public ActionResult Delete()
        {
            if (Entity.IsSystem)
            {
                return RedirectToIndex();
            }

            return View(Entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(DBWS_Entity model)
        {
            if (Entity.IsSystem)
            {
                return RedirectToIndex();
            }

            var updateValues = new List<string>() {
                nameof(DBWS_Entity.ID),
                nameof(DBWS_Entity.Name)
            };

            // Remove all keys, allow only needed
            foreach (var key in ModelState.Keys)
            {
                if (!updateValues.Contains(key))
                    ModelState.Remove(key);
            }

            // Before changing the entity name, check FK properties of other Entities
            foreach (var Ent2 in Application.Entities)
            {
                var fkEntity = Ent2.Constraints.FirstOrDefault(c => c.TypeID == (int)ConstraintType.ForeignKey && (c.GetForeignKeyProperties().FKEntity ?? string.Empty).Equals(Entity.Name));
                if (fkEntity is not null)
                {
                    ModelState.AddModelError("CustomError", $"Cannot rename entity as it is referenced by '{Ent2.Name}'");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(Entity);
            }

            try
            {
                DBContext.Remove(Entity);

                var apiResponse = await ApiHttpService.GetAsync($"{Application.Server.ServerUrl}/api/Application/DegenerateEntity?Entity={Entity.Name}", Application.Token, PortalUserAuthToken);
                
                apiResponse.Match(
                    jsonString => "OK",
                    errorHttpStatusCode =>
                    {
                        throw new Exception($"Could not get a response from Api server | Error code {errorHttpStatusCode}");
                    }
                );

                await DBContext.SaveChangesAsync();

                return RedirectToIndex();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(Entity);
            }
        }

        [HttpGet]
        public ActionResult Data()
        {
            return View((Application, Entity));
        }

        [HttpGet]
        public JsonResult GetProperties(int typeID)
        {
            return Json(GetPropertiesGroups((ReportType)typeID));
        }

        private PropertiesGroups GetPropertiesGroups(ReportType Type)
        {
            var result = new PropertiesGroups();

            if (Entity != null)
            {
                // Add primary key, count
                result.Properties.Add(new PropertyGroupItem()
                {
                    Name = Entity.Properties.Single(x => x.IsPrimaryKey).Name,
                    Subs = new List<string>() { DataAggregates.Count.ToString() }
                });

                foreach (var prop in Entity.Properties.Where(x => !x.IsPrimaryKey))
                {
                    switch (Type)
                    {
                        case ReportType.Grid:
                            if (prop.TypeID_Enum == PropertyType.String)
                            {
                                // Properties
                                result.Properties.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name,
                                    Subs = new List<string>()
                                    {
                                        DataAggregates.Max.ToString(),
                                        DataAggregates.Min.ToString()
                                    }
                                });

                                // Grouping
                                result.Groupings.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name
                                });
                            }
                            else if (prop.TypeID_Enum == PropertyType.Number)
                            {
                                // Properties
                                result.Properties.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name,
                                    Subs = new List<string>()
                                    {
                                        DataAggregates.Max.ToString(),
                                        DataAggregates.Min.ToString(),
                                        DataAggregates.Sum.ToString(),
                                        DataAggregates.Avg.ToString()
                                    }
                                });

                                // Grouping
                                result.Groupings.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name
                                });
                            }
                            else if (prop.TypeID_Enum == PropertyType.Boolean)
                            {
                                // Properties

                                // Grouping
                                result.Groupings.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name
                                });
                            }
                            else if (prop.TypeID_Enum == PropertyType.Date)
                            {
                                // Properties
                                result.Properties.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name,
                                    Subs = new List<string>()
                                    {
                                        DataAggregates.Max.ToString(),
                                        DataAggregates.Min.ToString()
                                    }
                                });

                                // Grouping
                                result.Groupings.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name,
                                    Subs = new List<string>() { "Year", "Month", "Day", "Hour", "Minute", "Second" }
                                });
                            }
                            break;
                        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                        case ReportType.Line:
                            if (prop.TypeID_Enum == PropertyType.Number)
                            {
                                // Properties
                                result.Properties.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name,
                                    Subs = new List<string>()
                                    {
                                        DataAggregates.Max.ToString(),
                                        DataAggregates.Min.ToString(),
                                        DataAggregates.Sum.ToString(),
                                        DataAggregates.Avg.ToString()
                                    }
                                });

                                // Grouping
                                result.Groupings.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name
                                });
                            }
                            else if (prop.TypeID_Enum == PropertyType.Date)
                            {
                                // Properties

                                // Grouping
                                result.Groupings.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name,
                                    Subs = new List<string>() { "Year", "Month", "Day", "Hour", "Minute", "Second" }
                                });
                            }
                            break;
                        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                        case ReportType.Pie:
                            if (prop.TypeID_Enum == PropertyType.String)
                            {
                                // Properties

                                // Grouping
                                result.Groupings.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name
                                });
                            }
                            else if (prop.TypeID_Enum == PropertyType.Number)
                            {
                                // Properties
                                result.Properties.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name,
                                    Subs = new List<string>()
                                    {
                                        DataAggregates.Max.ToString(),
                                        DataAggregates.Min.ToString(),
                                        DataAggregates.Sum.ToString(),
                                        DataAggregates.Avg.ToString()
                                    }
                                });

                                // Grouping
                                result.Groupings.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name
                                });
                            }
                            else if (prop.TypeID_Enum == PropertyType.Boolean)
                            {
                                // Properties

                                // Grouping
                                result.Groupings.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name
                                });
                            }
                            else if (prop.TypeID_Enum == PropertyType.Date)
                            {
                                // Properties

                                // Grouping
                                result.Groupings.Add(new PropertyGroupItem()
                                {
                                    Name = prop.Name,
                                    Subs = new List<string>() { "Year", "Month", "Day", "Hour", "Minute", "Second" }
                                });
                            }
                            break;
                    }
                }
            }

            return result;
        }
    }
}