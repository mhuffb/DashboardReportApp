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
                // Validation: return the form HTML back into the modal
                return PartialView("_ToolItemForm", model);
            }

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
        // Controllers/ToolingInventoryController.cs

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var model = await _svc.GetByIdAsync(id);
            if (model is null)
                return NotFound("Tooling item not found.");

            // return the same partial used by Create, but with an Id so it posts to Edit
            return PartialView("_ToolItemForm", model);
        }

    }
}
