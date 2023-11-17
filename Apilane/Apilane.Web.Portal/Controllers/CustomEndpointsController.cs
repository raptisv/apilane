using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Web.Portal.Abstractions;
using Apilane.Web.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Web.Portal.Controllers
{
    [Authorize]
    public class CustomEndpointsController : BaseWebApplicationController
    {
        public CustomEndpointsController(
            IPortalSettingsService portalSettingsService,
            ApplicationDbContext dbContext,
            IApiHttpService apiHttpService)
            : base(dbContext, apiHttpService)
        {

        }

        public ActionResult Index()
        {
            return View(Application);
        }

        private ActionResult RedirectToIndex()
        {
            return RedirectToRoute("AppRoute", new { appid = Application.Token, controller = "CustomEndpoints", action = "Index" });
        }

        [HttpGet]
        public ActionResult Create()
        {
            return View("AddEdit");
        }

        /// <summary>
        /// Need Name and Query
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        [HttpPost]
        public JsonResult GetUrl([FromBody] DBWS_CustomEndpoint item)
        {
            return Json(new {
                Params = item.GetParameters(),
                Url = item.GetUrl(Application.Server.ServerUrl, Application.Token, false)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DBWS_CustomEndpoint model)
        {
            ModelState.Remove(nameof(DBWS_CustomEndpoint.Application));

            model.Name = Utils.GetString(model.Name);

            var sameName = DBContext.CustomEndpoints.Where(x => x.AppID == Application.ID && x.Name.ToLower() == model.Name.ToLower()).ToList();

            if (sameName.Any())
            {
                ModelState.AddModelError(nameof(DBWS_CustomEndpoint.Name), $"Custom endpoint '{model.Name}' already exists");
            }

            ModelState.Remove(nameof(DBWS_CustomEndpoint.ID));
            ModelState.Remove(nameof(DBWS_CustomEndpoint.AppID));

            if (!ModelState.IsValid)
            {
                return View("AddEdit", model);
            }

            try
            {
                model.AppID = Application.ID;
                model.ID = 0;

                DBContext.CustomEndpoints.Add(model);
                await DBContext.SaveChangesAsync();

                return RedirectToIndex();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View("AddEdit", model);
            }
        }

        [HttpGet]
        public ActionResult Edit(long id)
        {
            var item = DBContext.CustomEndpoints.Single(x => x.ID == id);
            return View("AddEdit", item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(DBWS_CustomEndpoint model)
        {
            ModelState.Remove(nameof(DBWS_CustomEndpoint.Application));

            model.Name = Utils.GetString(model.Name);

            var sameName = DBContext.CustomEndpoints.Where(x => x.ID != model.ID && x.AppID == Application.ID && x.Name.Trim().ToLower() == model.Name.Trim().ToLower()).ToList();

            if (sameName.Any())
            {
                ModelState.AddModelError(nameof(DBWS_CustomEndpoint.Name), $"Custom endpoint '{model.Name}' already exists");
            }

            ModelState.Remove(nameof(DBWS_CustomEndpoint.AppID));

            if (!ModelState.IsValid)
            {
                return View("AddEdit", model);
            }

            try
            {
                var current = DBContext.CustomEndpoints.Single(x => x.ID == model.ID);

                DBContext.Entry(current).State = EntityState.Detached;

                current.Name = model.Name;
                current.Description = model.Description;
                current.Query = model.Query;

                DBContext.Attach(current);
                DBContext.Entry(current).Property(x => x.Name).IsModified = true;
                DBContext.Entry(current).Property(x => x.Description).IsModified = true;
                DBContext.Entry(current).Property(x => x.Query).IsModified = true;

                await DBContext.SaveChangesAsync();

                return RedirectToIndex();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View("AddEdit", model);
            }
        }

        [HttpGet]
        public IActionResult Delete(long id)
        {
            var item = DBContext.CustomEndpoints.Single(x => x.ID == id);
            return View(item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(DBWS_CustomEndpoint model)
        {
            try
            {
                var item = DBContext.CustomEndpoints.Single(x => x.ID == model.ID);
                DBContext.Remove(item);
                await DBContext.SaveChangesAsync();

                return RedirectToIndex();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(model);
            }
        }
    }
}