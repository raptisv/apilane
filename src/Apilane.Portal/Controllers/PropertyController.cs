using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Controllers
{
    [Authorize]
    public class PropertyController : BaseWebApplicationEntityPropertyController
    {
        public PropertyController(
            ApplicationDbContext dbContext,
            IApiHttpService apiHttpService)
            : base(dbContext, apiHttpService)
        {

        }

        public ActionResult Index()
        {
            return View(Entity.Properties);
        }

        private ActionResult RedirectToIndex()
        {
            return RedirectToRoute("EntRoute", new { appid = Application.Token, entid = Entity.Name, controller = "Entity", action = "Properties" });
        }

        private void Validate(ref DBWS_EntityProperty model)
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
                ModelState.AddModelError(nameof(DBWS_EntityProperty.Minimum), $"Min ({model.Minimum.Value}) cannot be greater than Max ({model.Maximum.Value})");
            }
        }

        [HttpGet]
        public ActionResult Edit()
        {
            if (EntityProperty is null)
            {
                throw new InvalidOperationException();
            }

            if (EntityProperty.IsSystem)
            {
                return RedirectToIndex();
            }

            return View(EntityProperty);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(DBWS_EntityProperty model)
        {
            if (EntityProperty is null)
            {
                throw new InvalidOperationException();
            }

            if (EntityProperty.IsSystem)
            {
                return RedirectToIndex();
            }

            var allowMaxEdit = EntityProperty.AllowMaxEdit();

            Validate(ref model);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                DBContext.Entry(EntityProperty).State = EntityState.Detached;

                DBContext.Attach(model);
                DBContext.Entry(model).Property(x => x.Description).IsModified = true;
                DBContext.Entry(model).Property(x => x.ValidationRegex).IsModified = true;
                DBContext.Entry(model).Property(x => x.Minimum).IsModified = true;
                if (allowMaxEdit)
                {
                    DBContext.Entry(model).Property(x => x.Maximum).IsModified = true;
                }

                await DBContext.SaveChangesAsync();

                return RedirectToIndex();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public ActionResult Rename()
        {
            if (EntityProperty is null)
            {
                throw new InvalidOperationException();
            }

            if (EntityProperty.IsSystem)
            {
                return RedirectToIndex();
            }

            return View(EntityProperty);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rename(DBWS_EntityProperty model)
        {
            if (EntityProperty is null)
            {
                throw new InvalidOperationException();
            }

            if (EntityProperty.IsSystem)
            {
                return RedirectToIndex();
            }

            List<string> updateValues = new List<string>() {
                nameof(DBWS_EntityProperty.Name)
            };

            // Remove all keys, allow only needed
            foreach (var key in ModelState.Keys)
            {
                if (!updateValues.Contains(key))
                    ModelState.Remove(key);
            }

            if (!ModelState.IsValid)
            {
                return View(EntityProperty);
            }

            try
            {
                EntityProperty.Name = model.Name;

                DBContext.Attach(EntityProperty);
                DBContext.Entry(EntityProperty).Property(x => x.Name).IsModified = true;

                var apiResponse = await ApiHttpService.GetAsync($"{Application.Server.ServerUrl}/api/Application/RenameEntityProperty?ID={EntityProperty.ID}&NewName={EntityProperty.Name}", Application.Token, PortalUserAuthToken);

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
                return View(EntityProperty);
            }
        }

        [HttpGet]
        public ActionResult Delete()
        {
            if (EntityProperty is null)
            {
                throw new InvalidOperationException();
            }

            if (EntityProperty.IsSystem)
            {
                return RedirectToIndex();
            }

            return View(EntityProperty);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(DBWS_EntityProperty model)
        {
            if (EntityProperty is null)
            {
                throw new InvalidOperationException();
            }

            if (EntityProperty.IsSystem)
            {
                return RedirectToIndex();
            }

            try
            {
                DBContext.Remove(EntityProperty);

                var apiResponse = await ApiHttpService.GetAsync($"{Application.Server.ServerUrl}/api/Application/DegenerateProperty?ID={EntityProperty.ID}", Application.Token, PortalUserAuthToken);

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
                return View(EntityProperty);
            }
        }
    }
}