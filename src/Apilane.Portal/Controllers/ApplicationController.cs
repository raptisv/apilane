using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Controllers
{
    [Authorize]
    public class ApplicationController : BaseWebApplicationController
    {
        private readonly ILogger<ApplicationController> _logger;
        private readonly PortalConfiguration _portalConfiguration;

        public ApplicationController(
            ILogger<ApplicationController> logger,
            ApplicationDbContext dbContext,
            IApiHttpService apiHttpService,
            PortalConfiguration portalConfiguration)
            : base(dbContext, apiHttpService)
        {
            _logger = logger;
            _portalConfiguration = portalConfiguration;
        }

        public IActionResult Entities()
        {
            return View(Application);
        }

        [HttpGet]
        public ActionResult EntityCreate()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EntityCreate(DBWS_Entity model)
        {
            ModelState.Remove(nameof(DBWS_Entity.Application));
            ModelState.Remove(nameof(DBWS_Entity.Properties));

            model.Name = Utils.GetString(model.Name);

            var sameName = DBContext.Entities.Where(x => x.AppID == Application.ID && x.Name.ToLower() == model.Name.ToLower()).ToList();

            if (sameName.Any())
            {
                ModelState.AddModelError(nameof(DBWS_Entity.Name), $"Entity '{model.Name}' already exists");
            }

            ModelState.Remove(nameof(DBWS_Entity.AppID));

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var apiSystemPropertiesResponse = await ApiHttpService.GetAsync($"{Application.Server.ServerUrl}/api/Application/GetSystemPropertiesAndConstraints?entityHasDifferentiationProperty={model.HasDifferentiationProperty}", Application.Token, PortalUserAuthToken);

                var initialPropertiesAndConstraints = apiSystemPropertiesResponse.Match(
                    jsonString =>
                    {
                        return JsonSerializer.Deserialize<EntityPropertiesConstrainsDto>(jsonString)
                            ?? throw new Exception($"Invalid response from Api server | Json response '{jsonString}'");
                    },
                    errorHttpStatusCode =>
                    {
                        throw new Exception($"Could not get a response from Api server | Error code {errorHttpStatusCode}");
                    });

                model.Properties = initialPropertiesAndConstraints.Properties;
                model.EntConstraints = JsonSerializer.Serialize(initialPropertiesAndConstraints.Constraints);
                model.IsReadOnly = false;
                model.IsSystem = false;
                model.AppID = Application.ID;
                model.ID = 0;
                model.Properties.ForEach(p => p.ID = 0);

                // Ad to DB
                DBContext.Entities.Add(model);

                var apiResponse = await ApiHttpService.PostAsync($"{Application.Server.ServerUrl}/api/Application/GenerateEntity", Application.Token, PortalUserAuthToken, model);

                apiResponse.Match(
                    jsonString => "OK",
                    errorHttpStatusCode =>
                    {
                        throw new Exception($"Could not get a response from Api server | Error code {errorHttpStatusCode}");
                    }
                );

                // Persist to DB
                await DBContext.SaveChangesAsync();

                return RedirectToRoute("AppRoute", new { appid = Application.Token, controller = "Application", action = "Entities" });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);

                return View(model);
            }
        }

        [HttpGet]
        public ActionResult Edit()
        {
            return View(Application);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(DBWS_Application model)
        {
            List<string> updateValues =
                model.DatabaseType == (int)DatabaseType.SQLLite
                ? new List<string>() { nameof(DBWS_Application.Name) }
                : new List<string>() { nameof(DBWS_Application.Name), nameof(DBWS_Application.ConnectionString) };

            // Remove all keys, allow only needed
            foreach (var key in ModelState.Keys)
            {
                if (!updateValues.Contains(key))
                {
                    ModelState.Remove(key);
                }
            }

            if (model.DatabaseType != (int)DatabaseType.SQLLite && string.IsNullOrWhiteSpace(model.ConnectionString))
            {
                ModelState.AddModelError((nameof(DBWS_Application.ConnectionString)), "Required");
            }

            if (!ModelState.IsValid)
            {
                return View(Application);
            }

            try
            {
                Application.Name = model.Name;

                if (model.DatabaseType != (int)DatabaseType.SQLLite)
                {
                    Application.ConnectionString = model.ConnectionString;
                }
                DBContext.Attach(Application);
                updateValues.ForEach(x => DBContext.Entry(Application).Property(x).IsModified = true);
                await DBContext.SaveChangesAsync();

                return RedirectToAction("Index", "Applications");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);

                return View(Application);
            }
        }

        [HttpGet]
        public ActionResult SetStatus()
        {
            return View(Application);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetStatus(DBWS_Application model)
        {
            try
            {
                Application.Online = !Application.Online;
                DBContext.Attach(Application);
                DBContext.Entry(Application).Property(x => x.Online).IsModified = true;
                await DBContext.SaveChangesAsync();

                return RedirectToAction("Index", "Applications");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public ActionResult Rebuild()
        {
            return View(Application);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rebuild(DBWS_Application model)
        {
            try
            {
                var apiResponse = await ApiHttpService.GetAsync($"{Application.Server.ServerUrl}/api/Application/Rebuild", Application.Token, PortalUserAuthToken);

                apiResponse.Match(
                    jsonString => "OK",
                    errorHttpStatusCode =>
                    {
                        throw new Exception($"Could not get a response from Api server | Error code {errorHttpStatusCode}");
                    }
                );

                return RedirectToAction("Index", "Applications");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public ActionResult Delete()
        {
            return View(Application);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(DBWS_Application model)
        {
            try
            {
                DBWS_Application application = DBContext.Applications.Single(x => x.ID == model.ID);

                DBContext.Remove(application);

                var apiResponse = await ApiHttpService.GetAsync($"{Application.Server.ServerUrl}/api/Application/Degenerate", Application.Token, PortalUserAuthToken);

                apiResponse.Match(
                    jsonString => "OK",
                    errorHttpStatusCode =>
                    {
                        throw new Exception($"Could not get a response from Api server | Error code {errorHttpStatusCode}");
                    }
                );

                await DBContext.SaveChangesAsync();

                return RedirectToAction("Index", "Applications");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Security(string? section = null, string? entity = null)
        {
            ViewBag.OpenSection = section;

            ViewBag.OpenEntity = entity;

            // Get roles from application.
            var applicationRoles = await GetApplicationRolesAsync(Application);

            // Get roles from existing configuration. There may be configured roles that do not exist anymore on application, for some reason (data deleted etc).
            var securityRoles = Application.Security_List.Select(x => x.RoleID).Where(x => !Globals.AUTHENTICATED.Equals(x) && !Globals.ANONYMOUS.Equals(x));

            // Concat & distinct
            var finalRolesList = securityRoles.Concat(applicationRoles).Distinct().ToList();

            ViewBag.AplicationRoles = finalRolesList;

            return View(Application);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Security(DBWS_Application model)
        {
            List<string> updateValues = new List<string>() {
                nameof(DBWS_Application.AuthTokenExpireMinutes),
                nameof(DBWS_Application.MaxAllowedFileSizeInKB),
                nameof(DBWS_Application.DateModified),
                nameof(DBWS_Application.ClientIPsLogic),
                nameof(DBWS_Application.ClientIPsValue),
                nameof(DBWS_Application.AllowUserRegister),
                nameof(DBWS_Application.AllowLoginUnconfirmedEmail),
                nameof(DBWS_Application.Security),
                nameof(DBWS_Application.ForceSingleLogin)
            };

            // Remove all keys, allow only needed            
            foreach (var key in ModelState.Keys)
            {
                if (!updateValues.Contains(key))
                {
                    ModelState.Remove(key);
                }
            }

            string? returnSection = null;
            foreach (string key in Request.Form.Keys)
            {
                if (key.Equals("return"))
                {
                    returnSection = Request.Form[key];
                }
            }

            string? returnEntity = null;
            foreach (string key in Request.Form.Keys)
            {
                if (key.Equals("entity"))
                {
                    returnEntity = Request.Form[key];
                }
            }

            List<string> IPAddresses = Utils.GetString(model.ClientIPsValue).Split(',').Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

            foreach (var item in IPAddresses)
            {
                if (!Utils.ValidateIPv4(item))
                {
                    ModelState.AddModelError(nameof(DBWS_Application.ClientIPsValue), $"{item} is not a valid IP address");
                }
            }

            if (!ModelState.IsValid)
            {
                return await Security(returnSection, returnEntity);
            }

            try
            {
                List<string> AuthValues = new List<string>();
                foreach (string key in Request.Form.Keys)
                {
                    if (key.StartsWith("Auth;"))
                    {
                        AuthValues.Add(Request.Form[key]!);
                    }
                }

                Application.AllowLoginUnconfirmedEmail = model.AllowLoginUnconfirmedEmail;
                Application.AllowUserRegister = model.AllowUserRegister;
                Application.ClientIPsLogic = model.ClientIPsLogic;
                Application.ClientIPsValue = string.Join(",", IPAddresses);
                Application.MaxAllowedFileSizeInKB = model.MaxAllowedFileSizeInKB;
                Application.AuthTokenExpireMinutes = model.AuthTokenExpireMinutes;
                Application.ForceSingleLogin = model.ForceSingleLogin;
                Application.DateModified = DateTime.UtcNow;

                List<DBWS_Security> SecurityItems = new List<DBWS_Security>();

                foreach (var item in AuthValues)
                {
                    string[] parts = item.Split(';');
                    string Name = Utils.GetString(parts[0]);
                    int TypeID = Utils.GetInt(parts[1], 0);
                    SecurityTypes SecurityType = (SecurityTypes)TypeID;
                    string Endpoint = Utils.GetString(parts[2]);
                    string RoleID = Utils.GetString(parts[3]);
                    int Record = Utils.GetInt(parts[4], -1);
                    string Properties = Utils.GetString(parts[5]);

                    var RateLimitType = EndpointRateLimit.None;
                    var RateLimitValue = 0;
                    if (parts.Length > 7)
                    {
                        RateLimitType = (EndpointRateLimit)Utils.GetInt(parts[6], 0);
                        RateLimitValue = Utils.GetInt(parts[7], 0);
                        if (RateLimitType != EndpointRateLimit.None && RateLimitValue <= 0)
                        {
                            throw new Exception($"Invalid rate limit request value");
                        }
                    }

                    if (!new List<string>() { "get", "post", "put", "delete" }.Contains(Endpoint.ToLower()))
                        throw new Exception($"Endpoint {Endpoint} is not suported");

                    if (!Enum.GetValues(typeof(EndpointRecordAuthorization)).Cast<EndpointRecordAuthorization>().ToList().Select(x => (int)x).Contains(Record))
                        throw new Exception($"Record level access {Record} is not suported");

                    List<DBWS_EntityProperty>? EntityProperties = null;

                    if (SecurityType == SecurityTypes.Entity) // If it is an Entity
                    {
                        var CurrentEntity = Application.Entities.SingleOrDefault(x => x.Name.Equals(Name))
                            ?? throw new Exception($"Invalid entity {Name}");

                        EntityProperties = CurrentEntity.Properties;
                    }
                    else if (SecurityType == SecurityTypes.CustomEndpoint) // If it is a custom endpoint
                    {
                        var CurrentCustomEndpoint = Application.CustomEndpoints.SingleOrDefault(x => x.Name.Equals(Name))
                            ?? throw new Exception($"Invalid item {Name}");
                    }

                    SecurityItems.Add(new DBWS_Security()
                    {
                        Name = Name,
                        TypeID = TypeID,
                        Action = Endpoint,
                        RoleID = RoleID,
                        Record = (int)Record,
                        Properties = SecurityType == SecurityTypes.Entity && EntityProperties is not null
                                    ? string.Join(",", Properties.Split(',').Where(x => !string.IsNullOrWhiteSpace(x) && EntityProperties.Select(y => y.Name).Contains(x)).Select(x => x.Trim()))
                                    : null,
                        RateLimit = RateLimitType != EndpointRateLimit.None && RateLimitValue > 0
                                    ? new DBWS_Security.RateLimitItem()
                                    {
                                        MaxRequests = RateLimitValue,
                                        TimeWindowType = (int)RateLimitType
                                    }
                                    : null
                    });
                }

                // Update securities
                Application.Security = JsonSerializer.Serialize(SecurityItems);

                DBContext.Attach(Application);
                updateValues.ForEach(x => DBContext.Entry(Application).Property(x).IsModified = true);
                await DBContext.SaveChangesAsync();

                return await Security(returnSection, returnEntity);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return await Security(returnSection, returnEntity);
            }
        }

        [HttpGet]
        public ActionResult UpdateFilterBuilder(string EntityName, string ExistingFilter)
        {
            ViewBag.ExistingFilter = ExistingFilter;
            return PartialView("FilterBuilder", Application.Entities.Single(x => x.Name.Equals(EntityName)));
        }

        [HttpGet]
        public ActionResult Email()
        {
            return View(Application);
        }

        private async Task<List<string>> GetApplicationRolesAsync(DBWS_Application app)
        {
            var apiResponse = await ApiHttpService.GetAsync($"{app.Server.ServerUrl}/api/Stats/Distinct?Entity=Users&Property=Roles", app.Token, PortalUserAuthToken);

            return apiResponse.Match(
                jsonString =>
                {
                    var obj = jsonString.DeserializeAnonymous(new[] { new { Roles = string.Empty } })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Roles))
                    .Select(x => x.Roles);

                    var roles = obj.Where(x => !x.Contains(',')).ToList();

                    foreach (var role in obj.Where(x => x.Contains(',')))
                    {
                        roles.AddRange(role.Split(',').Where(x => !string.IsNullOrWhiteSpace(x)));
                    }

                    return roles.Distinct().ToList();
                },
                errorHttpStatusCode =>
                {
                    return new List<string>();
                }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Email(DBWS_Application model)
        {
            List<string> updateValues = new List<string>() {
                nameof(DBWS_Application.MailServer),
                nameof(DBWS_Application.MailServerPort),
                nameof(DBWS_Application.MailFromAddress),
                nameof(DBWS_Application.MailFromDisplayName),
                nameof(DBWS_Application.MailUserName),
                nameof(DBWS_Application.MailPassword),
                nameof(DBWS_Application.EmailConfirmationRedirectUrl)
            };

            // Remove all keys, allow only needed
            foreach (var key in ModelState.Keys)
            {
                if (!updateValues.Contains(key))
                {
                    ModelState.Remove(key);
                }
            }

            if (!ModelState.IsValid)
            {
                return View(Application);
            }

            try
            {
                Application.MailServer = model.MailServer;
                Application.MailServerPort = model.MailServerPort;
                Application.MailFromAddress = model.MailFromAddress;
                Application.MailFromDisplayName = model.MailFromDisplayName;
                Application.MailUserName = model.MailUserName;
                Application.MailPassword = model.MailPassword;
                Application.EmailConfirmationRedirectUrl = model.EmailConfirmationRedirectUrl;

                DBContext.Attach(Application);
                updateValues.ForEach(x => DBContext.Entry(Application).Property(x).IsModified = true);
                await DBContext.SaveChangesAsync();

                return View(Application);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(Application);
            }
        }

        [HttpGet]
        public ActionResult DataBrowser(bool inFrame = false)
        {
            ViewBag.Layout = inFrame ? "~/Views/Shared/_LayoutData.cshtml" : "~/Views/Shared/_Layout.cshtml";
            return View(Application);
        }

        [HttpGet]
        public ActionResult Clone()
        {
            ViewBag.AvailableServers = DBContext.Servers.ToList();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Clone(ApplicationClone_DTO model)
        {
            try
            {
                ViewBag.AvailableServers = DBContext.Servers.ToList();

                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                var portalUserAuthToken = User.Identity?.GetPortalUserAuthToken()
                    ?? throw new Exception("User not logged in");

                var appServer = DBContext.Servers.Single(x => x.ID == model.ServerID)
                    ?? throw new Exception($"Server with ID {model.ServerID} not found");

                // Confirm connection string exists for non SQLLite data storages

                var isSqlite = model.DatabaseType == (int)DatabaseType.SQLLite;

                if (!isSqlite && string.IsNullOrWhiteSpace(model.ConnectionString))
                {
                    throw new Exception("Connection string is required");
                }

                // Serialize and deserialize application to deep clone
                var applicationToClone = JsonSerializer.Deserialize<DBWS_Application>(JsonSerializer.Serialize(Application))
                    ?? throw new Exception("Application json cannot be null");

                // Clear data
                applicationToClone.ID = 0;
                applicationToClone.Entities = applicationToClone.Entities.OrderBy(e => e.ID).ToList();
                applicationToClone.Entities.ForEach(e => e.Properties = e.Properties.OrderBy(p => p.ID).ToList());
                applicationToClone.Entities.ForEach(x => x.ID = 0);
                applicationToClone.Entities.ForEach(x => x.AppID = -1);
                applicationToClone.Entities.ForEach(x => x.Application = null!);
                applicationToClone.Entities.ForEach(x => x.Properties.ForEach(p => p.ID = 0));
                applicationToClone.Entities.ForEach(x => x.Properties.ForEach(p => p.EntityID = -1));
                applicationToClone.CustomEndpoints?.ForEach(x => x.ID = 0);
                applicationToClone.CustomEndpoints?.ForEach(x => x.AppID = -1);
                applicationToClone.CustomEndpoints?.ForEach(x => x.Application = null!);
                applicationToClone.Reports?.ForEach(x => x.ID = 0);
                applicationToClone.Reports?.ForEach(x => x.AppID = -1);
                applicationToClone.Reports?.ForEach(x => x.Application = null!);
                applicationToClone.Collaborates = null!;
                applicationToClone.Server = null!;

                // Set data
                applicationToClone.Token = Guid.NewGuid().ToString();
                applicationToClone.Name = applicationToClone.Name + " - Clone";
                applicationToClone.UserID = User.Identity.GetUserId();
                applicationToClone.AdminEmail = User.Identity.GetUserEmail();
                applicationToClone.ServerID = model.ServerID;
                applicationToClone.DatabaseType = model.DatabaseType;
                applicationToClone.ConnectionString = isSqlite ? null : model.ConnectionString;

                // !IMPORTANT! It is important to keep the same Encryption key since any encrypted data will not be able to be decrypted.

                DBContext.Applications.Add(applicationToClone);

                // Create application on api server
                var apiResponsGenerate = await ApiHttpService.PostAsync($"{appServer.ServerUrl}/api/ApplicationNew/Generate?installationKey={_portalConfiguration.InstallationKey}", applicationToClone.Token, portalUserAuthToken, applicationToClone);

                if (apiResponsGenerate.IsError(out var error))
                {
                    throw new Exception($"Could not get a response from Api server | Error code {error}");
                }

                await DBContext.SaveChangesAsync();

                if (model.CloneData)
                {
                    // Clone data
                    await CloneDataAsync(applicationToClone.Token, appServer);
                }

                return RedirectToRoute("AppRoute", new { appid = applicationToClone.Token, controller = "Application", action = "Entities" });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(model);
            }
        }

        private async Task CloneDataAsync(string appTokenTo, DBWS_Server serverTo)
        {
            var portalUserAuthToken = User.Identity?.GetPortalUserAuthToken()
                ?? throw new Exception("User not logged in");

            // Order by referenced entities, to avoid missing entities during constraint creation.
            var groups = Application.GroupEntitesByFKReferences();

            // All entities should be present in property "groups.Flat" with a level, depending on the FK chain.
            // An entity might be preset multiple times on the list due to many FK relationships, so it is important to take the maximum level of all occurrences.
            var entitiesOrderedByFKReferences = Application.Entities
                .OrderBy(e => groups.Flat.Where(x => x.ID.Equals(e.Name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Level).DefaultIfEmpty(0).Max());

            foreach(var entity in entitiesOrderedByFKReferences)
            {
                if (entity.Name.Equals("Files"))
                {
                    // Do not clone files
                    continue;
                }

                var entityData = await ApiHttpService.GetAllDataAsync(Application.Server.ServerUrl, Application.Token, entity.Name, portalUserAuthToken);

                if (entityData.IsError(out var error))
                {
                    throw new Exception(error.ToString());
                }

                int pageIndex = 0;
                int pageSize = 1000;
                var currentPage = entityData.Value.Data.Skip(pageIndex * pageSize).Take(pageSize).ToList();
                while (currentPage.Count > 0)
                {
                    // Importing 
                    var recordStart = pageIndex * pageSize;
                    var recordEnd = (pageIndex * pageSize) + pageSize;
                    var recordCount = entityData.Value.Data.Count;
                    recordEnd = recordCount >= recordEnd ? recordEnd : recordCount;
                    _logger.LogInformation($"Importing '{entity.Name}' records {recordStart}-{recordEnd}/{recordCount}");

                    // Insert current page
                    var insertedIds = await ApiHttpService.ImportDataAsync(serverTo.ServerUrl, appTokenTo, entity.Name, currentPage, portalUserAuthToken);

                    // Go to next page
                    pageIndex++;
                    currentPage = entityData.Value.Data.Skip(pageIndex * pageSize).Take(pageSize).ToList();
                }
            }
        }
    }
}