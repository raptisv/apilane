using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Web.Portal.Abstractions;
using Apilane.Web.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Web.Portal.Controllers
{
    [Authorize]
    public class ReportsController : BaseWebApplicationController
    {
        public ReportsController(
            ApplicationDbContext dbContext,
            IApiHttpService apiHttpService)
            : base(dbContext, apiHttpService)
        {

        }

        public ActionResult Index()
        {
            var reports = DBContext.Reports.Where(x => x.AppID == Application.ID).ToList();

            return View((Application, reports));
        }

        private ActionResult RedirectToIndex()
        {
            return RedirectToRoute("AppRoute", new { appid = Application.Token, controller = "Reports", action = "Index" });
        }

        private DBWS_ReportItem GetReport(long ID)
        {
            return DBContext.Reports.Single(x => x.AppID == Application.ID && x.ID == ID);
        }

        public class PropertiesGroups
        {
            public PropertiesGroups()
            {
                Properties = new List<PropertyGroupItem>();
                Groupings = new List<PropertyGroupItem>();
            }
            public List<PropertyGroupItem> Properties { get; set; }
            public List<PropertyGroupItem> Groupings { get; set; }
        }

        public class PropertyGroupItem
        {
            public PropertyGroupItem()
            {
                Subs = new List<string>();
            }

            public string Name { get; set; } = null!;
            public List<string> Subs { get; set; }
        }

        [HttpGet]
        public ActionResult Create()
        {
            return View("AddEdit");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DBWS_ReportItem model)
        {
            ModelState.Remove(nameof(DBWS_ReportItem.AppID));
            ModelState.Remove(nameof(DBWS_ReportItem.Filter));
            ModelState.Remove(nameof(DBWS_ReportItem.Application));

            var entity = Application.Entities.Single(x => x.Name == model.Entity);

            ValidateFilter(model.Filter);

            if (!ModelState.IsValid)
            {
                return View("AddEdit", model);
            }

            try
            {
                model.PanelWidth = 6;// Default col-md-6
                model.AppID = Application.ID;

                model.Order = DBContext.Reports.Where(x => x.AppID == Application.ID).ToList().Select(x => x.Order).DefaultIfEmpty(0).Max() + 1;
                model.ID = 0;
                DBContext.Reports.Add(model);
                await DBContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View("AddEdit", model);
            }

            return RedirectToIndex();
        }


        [HttpGet]
        public ActionResult Edit(long ID)
        {
            return View("AddEdit", GetReport(ID));
        }

        private void ValidateFilter(string? strFilter)
        {
            if (!string.IsNullOrWhiteSpace(strFilter))
            {
                try
                {
                    var filter = FilterData.Parse(strFilter)
                        ?? throw new Exception("Invalid filter");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(nameof(DBWS_ReportItem.Filter), ex.Message);
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(DBWS_ReportItem model)
        {
            ModelState.Remove(nameof(DBWS_ReportItem.Application));

            var entity = Application.Entities.Single(x => x.Name.Equals(model.Entity));

            ValidateFilter(model.Filter);

            if (!ModelState.IsValid)
            {
                return View("AddEdit", model);
            }

            DBWS_ReportItem report = GetReport(model.ID);
            try
            {
                report.TypeID = model.TypeID;
                report.Title = model.Title;
                report.Entity = model.Entity;
                report.Properties = model.Properties;
                report.GroupBy = model.GroupBy;
                report.Filter = model.Filter;
                report.MaxRecords = model.MaxRecords;
                report.DateModified = DateTime.UtcNow;

                DBContext.Attach(report);
                DBContext.Entry(report).Property(x => x.TypeID).IsModified = true;
                DBContext.Entry(report).Property(x => x.Title).IsModified = true;
                DBContext.Entry(report).Property(x => x.Entity).IsModified = true;
                DBContext.Entry(report).Property(x => x.Properties).IsModified = true;
                DBContext.Entry(report).Property(x => x.GroupBy).IsModified = true;
                DBContext.Entry(report).Property(x => x.Filter).IsModified = true;
                DBContext.Entry(report).Property(x => x.MaxRecords).IsModified = true;
                DBContext.Entry(report).Property(x => x.DateModified).IsModified = true;

                await DBContext.SaveChangesAsync();

                return RedirectToIndex();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View("AddEdit", report);
            }
        }

        [HttpGet]
        public ActionResult Delete(long ID)
        {
            return View(GetReport(ID));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(DBWS_ReportItem model)
        {
            try
            {
                var report = GetReport(model.ID);

                DBContext.Remove(report);
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
        public async Task<JsonResult> Reorder(string reportsOrder)
        {
            List<long> reportsIDs = Utils.GetString(reportsOrder).Split(',').Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Utils.GetLong(x)).Where(x => x > 0).ToList();

            var reports = DBContext.Reports.Where(x => x.AppID == Application.ID).ToList();

            for (int i = 0; i < reportsIDs.Count; i++)
            {
                var report = reports.FirstOrDefault(x => x.ID == reportsIDs[i]);
                if (report != null)
                {
                    report.Order = i;

                    DBContext.Attach(report);
                    DBContext.Entry(report).Property(x => x.Order).IsModified = true;
                }
            }

            await DBContext.SaveChangesAsync();

            return Json("OK");
        }

        [HttpGet]
        public async Task<JsonResult> SetWidth(long ID, int Width)
        {
            if (Width > 0 && Width <= 12 && Width % 2 == 0)
            {
                var report = DBContext.Reports.FirstOrDefault(x => x.ID == ID);
                if (report != null)
                {
                    report.PanelWidth = Width;

                    DBContext.Attach(report);
                    DBContext.Entry(report).Property(x => x.PanelWidth).IsModified = true;
                    await DBContext.SaveChangesAsync();
                }
            }

            return Json("OK");
        }
    }
}