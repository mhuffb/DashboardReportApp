using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    public class ToolingInventoryController : Controller
    {
        private readonly ToolingInventoryService _svc;

        public ToolingInventoryController(ToolingInventoryService svc)
        {
            _svc = svc;
        }

        // List
        public async Task<IActionResult> Index()
        {
            var items = await _svc.GetAllAsync();
            return View(items);
        }

        [HttpGet]
        public IActionResult Create(string? assembly = null)
        {
            var model = new ToolItemModel
            {
                AssemblyNumber = assembly ?? string.Empty,
                Category = ToolCategory.TopPunch,
                Condition = ToolCondition.ReadyForProduction,
                Status = ToolStatus.Available
            };
            return PartialView("_ToolItemForm", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ToolItemModel model, string? mode = null)
        {
            if (!ModelState.IsValid)
            {
                // Return the form back with validation messages
                return PartialView("_ToolItemForm", model);
            }

            await _svc.CreateAsync(model);

            // Fresh form after save:
            var fresh = new ToolItemModel
            {
                // RETAIN assembly when "Save & Add Another", otherwise clear it
                AssemblyNumber = string.Equals(mode, "add_more", StringComparison.OrdinalIgnoreCase)
                                 ? model.AssemblyNumber
                                 : string.Empty,
                Category = ToolCategory.TopPunch,
                Condition = ToolCondition.ReadyForProduction,
                Status = ToolStatus.Available
            };

            return PartialView("_ToolItemForm", fresh);
        }

        // EDIT keeps returning JSON so the client closes the modal & refreshes
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ToolItemModel model)
        {
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

    }
}
