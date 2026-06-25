using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Controllers
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
            var reports = DBContext.Reports
                .Include(x => x.Series)
                .Where(x => x.AppID == Application.ID)
                .ToList();

            return View((Application, reports));
        }

        private ActionResult RedirectToIndex()
        {
            return RedirectToRoute("AppRoute", new { appid = Application.Token, controller = "Reports", action = "Index" });
        }

        private DBWS_ReportPanel GetReport(long ID)
        {
            return DBContext.Reports
                .Include(x => x.Series)
                .Single(x => x.AppID == Application.ID && x.ID == ID);
        }

        /// <summary>
        /// Parses the series posted by the editor (JSON array) into model objects and validates them
        /// (adding ModelState errors). Rows are NOT dropped — the full set is returned so the editor
        /// can re-display exactly what the user entered when validation fails.
        /// </summary>
        private List<DBWS_ReportSeries> ParseSeries(string? seriesJson)
        {
            var result = new List<DBWS_ReportSeries>();

            List<DBWS_ReportSeries>? series = null;
            if (!string.IsNullOrWhiteSpace(seriesJson))
            {
                try
                {
                    series = JsonSerializer.Deserialize<List<DBWS_ReportSeries>>(seriesJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("CustomError", $"Invalid series: {ex.Message}");
                    return result;
                }
            }

            var order = 0;
            foreach (var s in series ?? new List<DBWS_ReportSeries>())
            {
                result.Add(new DBWS_ReportSeries
                {
                    ID = 0,
                    Label = (s.Label ?? string.Empty).Trim(),
                    Entity = (s.Entity ?? string.Empty).Trim(),
                    GroupBy = (s.GroupBy ?? string.Empty).Trim(),
                    Property = (s.Property ?? string.Empty).Trim(),
                    Filter = string.IsNullOrWhiteSpace(s.Filter) ? null : s.Filter,
                    Order = order++,
                    DateModified = DateTime.UtcNow
                });
            }

            // Validate without dropping rows (so edits are preserved on re-render).
            if (result.Count == 0)
            {
                ModelState.AddModelError("CustomError", "Add at least one series (label, entity, group-by and property).");
            }

            var incomplete = false;
            foreach (var s in result)
            {
                if (string.IsNullOrWhiteSpace(s.Label) ||
                    string.IsNullOrWhiteSpace(s.Entity) ||
                    string.IsNullOrWhiteSpace(s.GroupBy) ||
                    string.IsNullOrWhiteSpace(s.Property))
                {
                    incomplete = true;
                }
                else if (!Application.Entities.Any(e => e.Name.Equals(s.Entity, StringComparison.OrdinalIgnoreCase)))
                {
                    ModelState.AddModelError("CustomError", $"Unknown entity '{s.Entity}'.");
                }
                else
                {
                    ValidateFilter(s.Filter);
                }
            }

            if (incomplete)
            {
                ModelState.AddModelError("CustomError", "Each series needs a label, entity, group-by and property.");
            }

            return result;
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
        public async Task<IActionResult> Create(DBWS_ReportPanel model, string? seriesJson)
        {
            ModelState.Remove(nameof(DBWS_ReportPanel.AppID));
            ModelState.Remove(nameof(DBWS_ReportPanel.Application));

            var series = ParseSeries(seriesJson);

            // Keep the entered series so they are re-displayed if validation fails.
            model.Series = series;

            if (!ModelState.IsValid)
            {
                return View("AddEdit", model);
            }

            try
            {
                model.AppID = Application.ID;

                // Default geometry: half-width panel appended below the existing ones.
                model.W = 6;
                model.H = 4;
                model.X = 0;
                model.Y = DBContext.Reports.Where(x => x.AppID == Application.ID).ToList().Select(x => x.Y + x.H).DefaultIfEmpty(0).Max();
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
                    ModelState.AddModelError("CustomError", $"Invalid filter: {ex.Message}");
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(DBWS_ReportPanel model, string? seriesJson)
        {
            ModelState.Remove(nameof(DBWS_ReportPanel.Application));

            var series = ParseSeries(seriesJson);

            // Keep the entered series so they are re-displayed if validation/save fails.
            model.Series = series;

            if (!ModelState.IsValid)
            {
                return View("AddEdit", model);
            }

            DBWS_ReportPanel report = GetReport(model.ID);
            try
            {
                report.TypeID = model.TypeID;
                report.Title = model.Title;
                report.MaxRecords = model.MaxRecords;
                report.TimeRange = model.TimeRange;
                report.DateModified = DateTime.UtcNow;

                // Replace the series wholesale (cascade removes the old ones).
                DBContext.ReportSeries.RemoveRange(report.Series);
                report.Series = series;

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
        public ActionResult Delete(long ID)
        {
            return View(GetReport(ID));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(DBWS_ReportPanel model)
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

        public class LayoutItem
        {
            public long Id { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int W { get; set; }
            public int H { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> SaveLayout([FromBody] List<LayoutItem> layout)
        {
            if (layout == null || layout.Count == 0)
            {
                return Json("OK");
            }

            var reports = DBContext.Reports.Where(x => x.AppID == Application.ID).ToList();

            foreach (var item in layout)
            {
                var report = reports.FirstOrDefault(x => x.ID == item.Id);
                if (report == null)
                {
                    continue;
                }

                report.X = item.X;
                report.Y = item.Y;
                report.W = item.W;
                report.H = item.H;

                DBContext.Attach(report);
                DBContext.Entry(report).Property(x => x.X).IsModified = true;
                DBContext.Entry(report).Property(x => x.Y).IsModified = true;
                DBContext.Entry(report).Property(x => x.W).IsModified = true;
                DBContext.Entry(report).Property(x => x.H).IsModified = true;
            }

            await DBContext.SaveChangesAsync();

            return Json("OK");
        }
    }
}