using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")]
    public class ToolingInventoryController : Controller
    {
        private readonly ToolingInventoryService _svc;
        private readonly ToolingHistoryService _history;
        public ToolingInventoryController(ToolingInventoryService svc, ToolingHistoryService history)
        {
            _svc = svc;
            _history = history;
        }

        // List
        // Controllers/ToolingInventoryController.cs

        public async Task<IActionResult> Index()
        {
            var items = await _svc.GetAllAsync();

            var inProgress = _history.GetToolingHistories()
                .Where(h => !h.DateReceived.HasValue)
                .OrderByDescending(h => h.GroupID)
                .ToList();
            ViewBag.ToolingInProgress = inProgress;

            // presets for type-ahead
            ViewBag.ToolItemList = await _svc.GetDistinctToolItemsAsync();

            return View(items);
        }

        [HttpGet]
        public async Task<IActionResult> Create(string? assembly = null)
        {
            ViewBag.ToolItemList = await _svc.GetDistinctToolItemsAsync();

            var model = new ToolItemModel
            {
                AssemblyNumber = assembly?.Trim() ?? string.Empty,
                ToolItem = "", // ← leave blank (was "Top Punch")
                Condition = ToolCondition.ReadyForProduction,
                Status = ToolStatus.Available
            };
            return PartialView("_ToolItemForm", model);
        }


        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            ViewBag.ToolItemList = await _svc.GetDistinctToolItemsAsync();

            var model = await _svc.GetByIdAsync(id);
            if (model is null) return NotFound("Tooling item not found.");  
            return PartialView("_ToolItemForm", model);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ToolItemModel model, string? mode = null)
        {
            model.AssemblyNumber = model.AssemblyNumber?.Trim() ?? "";
            model.ToolNumber = model.ToolNumber?.Trim() ?? "";
            model.ToolItem = model.ToolItem?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(model.AssemblyNumber))
                ModelState.AddModelError(nameof(model.AssemblyNumber), "Assembly # is required.");
            if (string.IsNullOrWhiteSpace(model.ToolNumber))
                ModelState.AddModelError(nameof(model.ToolNumber), "Tool Number is required.");
            if (string.IsNullOrWhiteSpace(model.ToolItem))
                ModelState.AddModelError(nameof(model.ToolItem), "Tool Item is required.");

            if (!ModelState.IsValid)
                return PartialView("_ToolItemForm", model);

            var newId = await _svc.CreateAsync(model);
            var which = string.Equals(mode, "add_more", StringComparison.OrdinalIgnoreCase) ? "add_more" : "add_clear";

            return Json(new
            {
                ok = true,
                id = newId,
                mode = which,
                assembly = model.AssemblyNumber,
                message = $"Added tool {model.ToolNumber} to assembly {model.AssemblyNumber}."
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ToolItemModel model)
        {
            model.AssemblyNumber = model.AssemblyNumber?.Trim() ?? "";
            model.ToolNumber = model.ToolNumber?.Trim() ?? "";
            model.ToolItem = model.ToolItem?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(model.AssemblyNumber))
                ModelState.AddModelError(nameof(model.AssemblyNumber), "Assembly # is required.");
            if (string.IsNullOrWhiteSpace(model.ToolNumber))
                ModelState.AddModelError(nameof(model.ToolNumber), "Tool Number is required.");
            if (string.IsNullOrWhiteSpace(model.ToolItem))
                ModelState.AddModelError(nameof(model.ToolItem), "Tool Item is required.");

            if (!ModelState.IsValid)
                return PartialView("_ToolItemForm", model);

            await _svc.UpdateAsync(model);
            return Json(new { ok = true });
        }


        private bool IsAjax()
        {
            if (Request?.Headers is null) return false;
            if (!Request.Headers.TryGetValue("X-Requested-With", out var header)) return false;
            return string.Equals(header.ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        }
        // Controllers/ToolingInventoryController.cs

     

    }
}
