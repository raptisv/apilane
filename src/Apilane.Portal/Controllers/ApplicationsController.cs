using Apilane.Common;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Common.Utilities;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static Apilane.Common.Models.AggregateData;

namespace Apilane.Portal.Controllers
{
    [Authorize]
    public class ApplicationsController : BaseWebController
    {
        private readonly PortalConfiguration _portalConfiguration;
        private readonly IEmailService _emailService;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IPortalSettingsService _portalSettingsService;

        public ApplicationsController(
            ApplicationDbContext dbContext,
            IApiHttpService apiHttpService,
            IEmailService emailService,
            IWebHostEnvironment webHostEnvironment,
            IPortalSettingsService portalSettingsService,
            PortalConfiguration portalConfiguration)
            : base(dbContext, apiHttpService)
        {
            _emailService = emailService;
            _webHostEnvironment = webHostEnvironment;
            _portalSettingsService = portalSettingsService;
            _portalConfiguration = portalConfiguration;
        }

        public ActionResult Index()
        {
            var servers = DBContext.Servers.ToList();

            return View((Applications, servers));
        }

        [HttpGet]
        public ActionResult Create()
        {
            var userId = User.Identity?.GetUserId()
                ?? throw new Exception("User not logged in");

            ViewBag.AvailableServers = DBContext.Servers.ToList();

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DBWS_ApplicationNew_Dto model)
        {
            ModelState.Remove(nameof(DBWS_ApplicationNew_Dto.Token));
            ModelState.Remove(nameof(DBWS_ApplicationNew_Dto.EncryptionKey));

            var userId = User.Identity?.GetUserId()
                ?? throw new Exception("User not logged in");

            ViewBag.AvailableServers = DBContext.Servers.ToList();

            // Confirm connection string exists for non SQLLite data storages

            var isSqlite = model.DatabaseType == (int)DatabaseType.SQLLite;

            if (!isSqlite && string.IsNullOrWhiteSpace(model.ConnectionString))
            {
                ModelState.AddModelError((nameof(DBWS_ApplicationNew_Dto.ConnectionString)), "Required");
            }

            // Server validations

            var appServer = DBContext.Servers.Single(x => x.ID == model.ServerID);

            // If differentiation property is set, make sure it is not named like a system property

            model.DifferentiationEntity = Utils.GetString(model.DifferentiationEntity);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                model.Token = Guid.NewGuid().ToString();
                model.EncryptionKey = Utils.RandomString(8);

                string differentiationEntity = Utils.GetString(model.DifferentiationEntity);

                var apiResponse = await ApiHttpService.GetAsync($"{appServer.ServerUrl}/api/ApplicationNew/GetSystemEntities?differentiationEntity={differentiationEntity}", string.Empty, PortalUserAuthToken);

                var initialEntities = apiResponse.Match(
                    jsonString =>
                    {
                        return JsonSerializer.Deserialize<List<DBWS_Entity>>(jsonString)
                            ?? throw new Exception($"Invalid response from Api server | Json response '{jsonString}'");
                    },
                    errorHttpStatusCode =>
                    {
                        throw new Exception($"Could not get a response from Api server | Error code {errorHttpStatusCode}");
                    });

                var newApplication = new DBWS_Application()
                {
                    // Basic data
                    AdminEmail = User.Identity.GetUserEmail(),
                    Entities = initialEntities,
                    Name = model.Name,
                    Token = model.Token,
                    DatabaseType = model.DatabaseType,
                    EncryptionKey = model.EncryptionKey.Encrypt(Globals.EncryptionKey),
                    ConnectionString = isSqlite ? null : model.ConnectionString,
                    ServerID = model.ServerID,
                    DifferentiationEntity = model.DifferentiationEntity,
                    // Rest of default info
                    UserID = User.Identity.GetUserId(),
                    Online = true,
                    AllowLoginUnconfirmedEmail = true,
                    AllowUserRegister = true,
                    AuthTokenExpireMinutes = 60,
                    MaxAllowedFileSizeInKB = 100
                };

                // Create initial report

                var initReport = new DBWS_ReportItem()
                {
                    ID = 0,
                    AppID = -1,
                    Title = "User registrations per day",
                    Entity = "Users",
                    DateModified = DateTime.UtcNow,
                    MaxRecords = 1000,
                    Order = 0,
                    PanelWidth = 12,
                    GroupBy = $"Created.Year,Created.Month,Created.Day",
                    Properties = $"ID.{DataAggregates.Count.ToString()}",
                    TypeID = (int)ReportType.Line,
                    Filter = null
                };

                // Initialize values
                newApplication.Entities.ForEach(x => x.ID = 0);
                newApplication.Entities.ForEach(x => x.Properties.ForEach(p => p.ID = 0));
                newApplication.Reports = new List<DBWS_ReportItem>() { initReport };
                DBContext.Applications.Add(newApplication);

                // Create application on api server
                var apiResponsGenerate = await ApiHttpService.PostAsync($"{appServer.ServerUrl}/api/ApplicationNew/Generate?installationKey={_portalConfiguration.InstallationKey}", newApplication.Token, PortalUserAuthToken, newApplication);

                apiResponsGenerate.Match(
                    jsonString => "OK",
                    errorHttpStatusCode =>
                    {
                        throw new Exception($"Could not get a response from Api server | Error code {errorHttpStatusCode}");
                    }
                );

                await DBContext.SaveChangesAsync();

                // Before redirect, reset the app on api side to prevent any cached error
                await ResetAppAsync(appServer.ServerUrl, model.Token, PortalUserAuthToken);

                return RedirectToRoute("AppRoute", new { appid = model.Token, controller = "Application", action = "Entities" });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult Import()
        {
            ViewBag.AvailableServers = DBContext.Servers.ToList();
            return View();
        }

        [HttpPost]
        public async Task<JsonResult> Import(
            [FromForm] IFormFile fileUpload,
            int ServerID,
            int DatabaseType,
            string? ConnectionString)
        {
            try
            {
                if (fileUpload == null || fileUpload.Length == 0)
                {
                    throw new Exception("No file found to upload");
                }

                byte[] buffer;
                using (var ms = new MemoryStream())
                {
                    fileUpload.CopyTo(ms);
                    buffer = ms.ToArray();
                }

                var importedApplication = JsonSerializer.Deserialize<DBWS_Application>(buffer)
                    ?? throw new Exception("Application json cannot be null");

                var portalUserAuthToken = User.Identity?.GetPortalUserAuthToken()
                    ?? throw new Exception("User not logged in");

                var allApplications = DBContext.Applications.ToList();
                if (allApplications.Any(x => x.Token.Equals(importedApplication.Token)))
                {
                    throw new Exception($"Application token '{importedApplication.Token}' already exists");
                }

                var appServer = DBContext.Servers.Single(x => x.ID == ServerID)
                    ?? throw new Exception($"Server with ID {ServerID} not found");

                // Confirm connection string exists for non SQLLite data storages

                var isSqlite = DatabaseType == (int)Apilane.Common.Enums.DatabaseType.SQLLite;

                if (!isSqlite && string.IsNullOrWhiteSpace(ConnectionString))
                {
                    throw new Exception("Connection string is required");
                }

                // Clear data
                importedApplication.ID = 0;
                importedApplication.Entities = importedApplication.Entities.OrderBy(e => e.ID).ToList();
                importedApplication.Entities.ForEach(e => e.Properties = e.Properties.OrderBy(p => p.ID).ToList());
                importedApplication.Entities.ForEach(x => x.ID = 0);
                importedApplication.Entities.ForEach(x => x.AppID = -1);
                importedApplication.Entities.ForEach(x => x.Application = null!);
                importedApplication.Entities.ForEach(x => x.Properties.ForEach(p => p.ID = 0));
                importedApplication.Entities.ForEach(x => x.Properties.ForEach(p => p.EntityID = -1));
                importedApplication.CustomEndpoints?.ForEach(x => x.ID = 0);
                importedApplication.CustomEndpoints?.ForEach(x => x.AppID = -1);
                importedApplication.CustomEndpoints?.ForEach(x => x.Application = null!);
                importedApplication.Reports?.ForEach(x => x.ID = 0);
                importedApplication.Reports?.ForEach(x => x.AppID = -1);
                importedApplication.Reports?.ForEach(x => x.Application = null!);
                importedApplication.Collaborates = null!;
                importedApplication.Server = null!;

                // Set data
                importedApplication.UserID = User.Identity.GetUserId();
                importedApplication.AdminEmail = User.Identity.GetUserEmail();
                importedApplication.ServerID = ServerID;
                importedApplication.DatabaseType = DatabaseType;
                importedApplication.ConnectionString = isSqlite ? null : ConnectionString;

                // !IMPORTANT! It is important to keep the same Encryption key since any encrypted data will not be able to be decrypted.

                DBContext.Applications.Add(importedApplication);

                // Create application on api server
                var apiResponsGenerate = await ApiHttpService.PostAsync($"{appServer.ServerUrl}/api/ApplicationNew/Generate?installationKey={_portalConfiguration.InstallationKey}", importedApplication.Token, portalUserAuthToken, importedApplication);

                if (apiResponsGenerate.IsError(out var error))
                {
                    throw new Exception($"Could not get a response from Api server | Error code {error}");
                }

                await DBContext.SaveChangesAsync();

                var redirectUrl = Url.RouteUrl("AppRoute", new { appid = importedApplication.Token, controller = "Application", action = "Entities" });
                return Json(redirectUrl);
            }
            catch (Exception ex)
            {
                HttpContext.Response.StatusCode = 500;
                return Json(ex.Message);
            }
        }

        [HttpGet]
        public ActionResult CompareApplications(string appTokenSource, string appTokenTarget)
        {
            if (string.IsNullOrWhiteSpace(appTokenSource) ||
                string.IsNullOrWhiteSpace(appTokenTarget))
            {
                return BadRequest($"App token cannot be empty");
            }

            if (appTokenSource.Equals(appTokenTarget))
            {
                return BadRequest($"Cannot compare to self");
            }

            var application_1 = Applications.SingleOrDefault(x => x.Token.Equals(appTokenSource));

            if (application_1 is null)
            {
                return BadRequest($"Application '{appTokenSource}' not found");
            }

            var application_2 = Applications.SingleOrDefault(x => x.Token.Equals(appTokenTarget));

            if (application_2 is null)
            {
                return BadRequest($"Application '{appTokenTarget}' not found");
            }

            // Entities

            var entitiesAdded = application_2.Entities.Where(e => !application_1.Entities.Select(x => x.Name).Contains(e.Name));
            var entitiesRemoved = application_1.Entities.Where(e => !application_2.Entities.Select(x => x.Name).Contains(e.Name));

            var entitesChanged = new List<object>();
            foreach (var entity1 in application_1.Entities)
            {
                var entity2 = application_2.Entities.SingleOrDefault(e2 => e2.Name.Equals(entity1.Name));
                if (entity2 is not null)
                {
                    var propertiesAdded   = entity2.Properties.Where(p => !p.IsSystem && !p.IsPrimaryKey && !entity1.Properties.Select(x => x.Name).Contains(p.Name)).ToList();
                    var propertiesRemoved = entity1.Properties.Where(p => !p.IsSystem && !p.IsPrimaryKey && !entity2.Properties.Select(x => x.Name).Contains(p.Name)).ToList();

                    // Field-level changes for properties that exist in both apps
                    var propertiesChanged = new List<object>();
                    foreach (var prop1 in entity1.Properties.Where(p => !p.IsSystem && !p.IsPrimaryKey))
                    {
                        var prop2 = entity2.Properties.FirstOrDefault(p =>
                            p.Name.Equals(prop1.Name, StringComparison.OrdinalIgnoreCase)
                            && !p.IsSystem && !p.IsPrimaryKey);
                        if (prop2 is null) continue;

                        var fieldChanges = new List<object>();
                        if (prop1.TypeID != prop2.TypeID)
                            fieldChanges.Add(new { Field = "Type", Before = prop1.TypeID_Enum.ToString(), After = prop2.TypeID_Enum.ToString() });
                        if (prop1.Required != prop2.Required)
                            fieldChanges.Add(new { Field = "Required", Before = prop1.Required.ToString(), After = prop2.Required.ToString() });
                        if (prop1.Minimum != prop2.Minimum)
                            fieldChanges.Add(new { Field = "Minimum", Before = prop1.Minimum?.ToString(), After = prop2.Minimum?.ToString() });
                        if (prop1.Maximum != prop2.Maximum)
                            fieldChanges.Add(new { Field = "Maximum", Before = prop1.Maximum?.ToString(), After = prop2.Maximum?.ToString() });
                        if (prop1.DecimalPlaces != prop2.DecimalPlaces)
                            fieldChanges.Add(new { Field = "DecimalPlaces", Before = prop1.DecimalPlaces?.ToString(), After = prop2.DecimalPlaces?.ToString() });
                        if (prop1.Encrypted != prop2.Encrypted)
                            fieldChanges.Add(new { Field = "Encrypted", Before = prop1.Encrypted.ToString(), After = prop2.Encrypted.ToString() });
                        if (!string.Equals(prop1.ValidationRegex, prop2.ValidationRegex, StringComparison.Ordinal))
                            fieldChanges.Add(new { Field = "ValidationRegex", Before = prop1.ValidationRegex, After = prop2.ValidationRegex });
                        if (!string.Equals(prop1.Description, prop2.Description, StringComparison.Ordinal))
                            fieldChanges.Add(new { Field = "Description", Before = prop1.Description, After = prop2.Description });

                        if (fieldChanges.Any())
                            propertiesChanged.Add(new { prop1.Name, Changes = fieldChanges });
                    }

                    // Entity metadata changes
                    var metadataChanges = new List<object>();
                    if (!string.Equals(entity1.Description, entity2.Description, StringComparison.Ordinal))
                        metadataChanges.Add(new { Field = "Description", Before = entity1.Description, After = entity2.Description });
                    if (entity1.RequireChangeTracking != entity2.RequireChangeTracking)
                        metadataChanges.Add(new { Field = "RequireChangeTracking", Before = entity1.RequireChangeTracking.ToString(), After = entity2.RequireChangeTracking.ToString() });
                    if (entity1.HasDifferentiationProperty != entity2.HasDifferentiationProperty)
                        metadataChanges.Add(new { Field = "HasDifferentiationProperty", Before = entity1.HasDifferentiationProperty.ToString(), After = entity2.HasDifferentiationProperty.ToString() });

                    var e1ConstraintKeys = entity1.Constraints
                        .Select(c => $"{c.TypeID}|{c.Properties?.Trim().ToLowerInvariant()}")
                        .ToHashSet();
                    var e2ConstraintKeys = entity2.Constraints
                        .Select(c => $"{c.TypeID}|{c.Properties?.Trim().ToLowerInvariant()}")
                        .ToHashSet();
                    var constraintsAdded   = entity2.Constraints
                        .Where(c => !c.IsSystem && !string.IsNullOrWhiteSpace(c.Properties)
                            && !e1ConstraintKeys.Contains($"{c.TypeID}|{c.Properties?.Trim().ToLowerInvariant()}"))
                        .ToList();
                    var constraintsRemoved = entity1.Constraints
                        .Where(c => !c.IsSystem && !string.IsNullOrWhiteSpace(c.Properties)
                            && !e2ConstraintKeys.Contains($"{c.TypeID}|{c.Properties?.Trim().ToLowerInvariant()}"))
                        .ToList();

                    if (propertiesAdded.Any() || propertiesRemoved.Any() || propertiesChanged.Any() ||
                        constraintsAdded.Any() || constraintsRemoved.Any() || metadataChanges.Any())
                    {
                        entitesChanged.Add(new
                        {
                            Name = entity1.Name,
                            MetadataChanges   = metadataChanges,
                            PropertiesAdded   = propertiesAdded.Select(p => new
                            {
                                p.Name,
                                TypeLabel = p.TypeID_Enum.ToString(),
                                p.TypeID,
                                p.Required,
                                p.Minimum,
                                p.Maximum,
                                p.DecimalPlaces,
                                p.Encrypted,
                                p.ValidationRegex,
                                p.Description
                            }),
                            PropertiesChanged = propertiesChanged,
                            PropertiesRemoved = propertiesRemoved.Select(p => new
                            {
                                p.Name,
                                TypeLabel = p.TypeID_Enum.ToString()
                            }),
                            ConstraintsAdded   = constraintsAdded.Select(c => new { c.TypeID, c.Properties }),
                            ConstraintsRemoved = constraintsRemoved.Select(c => new { c.TypeID, c.Properties })
                        });
                    }
                }
            }

            // Custom endpoints

            var customEndpointsAdded = application_2.CustomEndpoints.Where(e => !application_1.CustomEndpoints.Select(x => x.Name).Contains(e.Name));
            var customEndpointsRemoved = application_1.CustomEndpoints.Where(e => !application_2.CustomEndpoints.Select(x => x.Name).Contains(e.Name));

            var customEndpointsChanged = new List<object>();
            foreach (var customEndpoint1 in application_1.CustomEndpoints)
            {
                var customEndpoint2 = application_2.CustomEndpoints.SingleOrDefault(e2 => e2.Name.Equals(customEndpoint1.Name));
                if (customEndpoint2 is not null)
                {
                    var queryChanged       = !customEndpoint1.Query.Equals(customEndpoint2.Query);
                    var descriptionChanged = !string.Equals(customEndpoint1.Description, customEndpoint2.Description, StringComparison.Ordinal);
                    if (queryChanged || descriptionChanged)
                    {
                        customEndpointsChanged.Add(new
                        {
                            Name              = customEndpoint1.Name,
                            DescriptionBefore = customEndpoint1.Description,
                            DescriptionAfter  = customEndpoint2.Description,
                            QueryBefore       = customEndpoint1.Query,
                            QueryAfter        = customEndpoint2.Query
                        });
                    }
                }
            }

            // Security

            var securityAdded = application_2.Security_List
                .Where(s =>
                    SecurityItemExists(application_2, s) &&
                    !application_1.Security_List.Where(s => SecurityItemExists(application_1, s)).Select(x => x.ToUniqueStringShort()).Contains(s.ToUniqueStringShort())
                );
            var securityRemoved = application_1.Security_List
                .Where(s =>
                    SecurityItemExists(application_1, s) &&
                    !application_2.Security_List.Where(s => SecurityItemExists(application_2, s)).Select(x => x.ToUniqueStringShort()).Contains(s.ToUniqueStringShort())
                );

            var securityChanged = new List<object>();
            foreach (var security1 in application_1.Security_List.Where(s => SecurityItemExists(application_1, s)))
            {
                var security2 = application_2.Security_List.Where(s => SecurityItemExists(application_2, s))
                    .SingleOrDefault(e2 => e2.ToUniqueStringShort().Equals(security1.ToUniqueStringShort()));

                if (security2 is not null)
                {
                    if (!security1.ToUniqueStringLong().Equals(security2.ToUniqueStringLong()))
                    {
                        securityChanged.Add(new
                        {
                            Name = security1.NameDescriptive(),
                            SecurityBefore = new
                            {
                                Name = security1.NameDescriptive(),
                                Role = security1.RoleID,
                                Type = security1.TypeID_Enum.ToString(),
                                RateLimit = security1.RateLimit?.ToUniqueString(),
                                security1.Action,
                                Record = ((EndpointRecordAuthorization)security1.Record).ToString(),
                                Properties = string.Join(",", security1.GetProperties().OrderBy(x => x))
                            },
                            SecurityAfter = new
                            {
                                Name = security2.NameDescriptive(),
                                Role = security2.RoleID,
                                Type = security2.TypeID_Enum.ToString(),
                                RateLimit = security2.RateLimit?.ToUniqueString(),
                                security2.Action,
                                Record = ((EndpointRecordAuthorization)security2.Record).ToString(),
                                Properties = string.Join(",", security2.GetProperties().OrderBy(x => x))
                            }
                        });
                    }
                }
            }

            // Security items are not updated when an Entity or a Custom Endpoint are updated/delete so some data might be out of sync.
            // Use this to confirm the security item is valid;
            bool SecurityItemExists(DBWS_Application application, DBWS_Security security)
            {
                if (security.TypeID_Enum == SecurityTypes.Entity)
                {
                    return application.Entities.Any(x => x.Name.Equals(security.Name));
                }

                if (security.TypeID_Enum == SecurityTypes.CustomEndpoint)
                {
                    return application.CustomEndpoints.Any(x => x.Name.Equals(security.Name));
                }

                return false;
            }

            return Json(new
            {
                ApplicationSource = application_1.Name,
                ApplicationTarget = application_2.Name,
                Entities = new
                {
                    Added = entitiesAdded.Select(e => new
                    {
                        e.Name,
                        e.Description,
                        e.RequireChangeTracking,
                        e.HasDifferentiationProperty,
                        Properties = e.Properties
                            .Where(p => !p.IsSystem && !p.IsPrimaryKey)
                            .Select(p => new
                            {
                                p.Name,
                                TypeLabel = p.TypeID_Enum.ToString(),
                                p.TypeID,
                                p.Required,
                                p.Minimum,
                                p.Maximum,
                                p.DecimalPlaces,
                                p.Encrypted,
                                p.ValidationRegex,
                                p.Description
                            }),
                        Constraints = e.Constraints
                            .Where(c => !c.IsSystem && !string.IsNullOrWhiteSpace(c.Properties))
                            .Select(c => new { c.TypeID, c.Properties })
                    }),
                    Removed = entitiesRemoved.Select(e => new
                    {
                        e.Name,
                        e.Description,
                        e.RequireChangeTracking,
                        e.HasDifferentiationProperty,
                        Properties = e.Properties
                            .Where(p => !p.IsSystem && !p.IsPrimaryKey)
                            .Select(p => new
                            {
                                p.Name,
                                TypeLabel = p.TypeID_Enum.ToString(),
                                p.TypeID,
                                p.Required,
                                p.Minimum,
                                p.Maximum,
                                p.DecimalPlaces,
                                p.Encrypted,
                                p.ValidationRegex,
                                p.Description
                            }),
                        Constraints = e.Constraints
                            .Where(c => !c.IsSystem && !string.IsNullOrWhiteSpace(c.Properties))
                            .Select(c => new { c.TypeID, c.Properties })
                    }),
                    Changed = entitesChanged
                },
                CustomEndpoints = new
                {
                    Added = customEndpointsAdded.Select(ce => new
                    {
                        ce.Name,
                        ce.Description,
                        ce.Query
                    }),
                    Removed = customEndpointsRemoved.Select(ce => new
                    {
                        ce.Name,
                        ce.Description,
                        ce.Query
                    }),
                    Changed = customEndpointsChanged
                },
                Security = new
                {
                    Added = securityAdded.Select(x => new
                    {
                        Name = x.NameDescriptive(),
                        Role = x.RoleID,
                        Type = x.TypeID_Enum.ToString(),
                        RateLimit = x.RateLimit?.ToUniqueString() ?? "none",
                        x.Action,
                        Record = ((EndpointRecordAuthorization)x.Record).ToString(),
                        Properties = string.Join(",", x.GetProperties().OrderBy(x => x))
                    }),
                    Removed = securityRemoved.Select(x => new
                    {
                        Name = x.NameDescriptive(),
                        Role = x.RoleID,
                        Type = x.TypeID_Enum.ToString(),
                        RateLimit = x.RateLimit?.ToUniqueString() ?? "none",
                        x.Action,
                        Record = ((EndpointRecordAuthorization)x.Record).ToString(),
                        Properties = string.Join(",", x.GetProperties().OrderBy(x => x))
                    }),
                    Changed = securityChanged
                }
            });
        }
    }
}