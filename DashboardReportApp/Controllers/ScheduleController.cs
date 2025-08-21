using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Text.Json;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")] // Set your password here

    [Route("Schedule")]
    public class ScheduleController : Controller
    {
        private readonly ScheduleService _scheduleService;

        public ScheduleController(ScheduleService scheduleService)
        {
            _scheduleService = scheduleService;
        }

        public IActionResult Index(string masterId = null, int quantity = 0)
        {
            var viewModel = new ScheduleModel
            {
                AllComponents = new List<SintergyComponent>(),
                AllParts = _scheduleService.GetAllSchedParts()
            };

            if (!string.IsNullOrEmpty(masterId) && quantity > 0)
            {
                viewModel.AllComponents = _scheduleService.GetComponentsForMasterId(masterId.ToUpper(), quantity);
            }

            return View(viewModel);
        }

        [HttpPost("ScheduleComponents")]
        public IActionResult ScheduleComponents(ScheduleModel viewModel)
        {
            try
            {
                if (viewModel.AllComponents == null || !viewModel.AllComponents.Any())
                {
                    TempData["Error"] = "No components found to schedule.";
                    return RedirectToAction("Index");
                }

                if (string.IsNullOrEmpty(viewModel.AllComponents.FirstOrDefault()?.MasterId))
                {
                    TempData["Error"] = "An error occurred while scheduling: Part number (MasterId) cannot be null.";
                    return RedirectToAction("Index");
                }

                _scheduleService.ScheduleComponents(viewModel);
                TempData["Success"] = "Parts and components successfully scheduled!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"An error occurred while scheduling: {ex.Message}";
            }

            return RedirectToAction("Index");
        }

        [HttpPost("UpdatePart")]
        public IActionResult UpdatePart(SintergyComponent updatedPart)
        {
            try
            {
                _scheduleService.UpdatePart(updatedPart);
                TempData["Success"] = "Part updated successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"An error occurred while updating part: {ex.Message}";
            }
            return RedirectToAction("Index");
        }
        [HttpGet("GetComponents")]
        public IActionResult GetComponents(string part, int quantity)
        {
            // Log parameters
            Console.WriteLine($"GetComponents called with part: {part}, quantity: {quantity}");
            var components = _scheduleService.GetComponentsForMasterId(part, quantity);
            Console.WriteLine($"Components count: {components.Count}");

            // Build a new model with these components.
            var model = new ScheduleModel
            {
                AllComponents = components
            };

            // Return the partial view with the model.
            return PartialView("_ComponentsPartial", model);
        }
        [HttpPost("ReceivePowder")]
        public IActionResult ReceivePowder(IFormFile pdf, bool confirm = false, int lot = 0, decimal weight = 0, string material = null)
        {
            try
            {
                if (pdf == null || pdf.Length == 0)
                    return Json(new { success = false, message = "No file selected." });

                byte[] bytes;
                using (var ms = new MemoryStream())
                {
                    pdf.CopyTo(ms);
                    bytes = ms.ToArray();
                }

                if (!confirm)
                {
                    // parse only
                    var (pLot, pWeight, pMaterial) = _scheduleService.ParsePowderPdf(bytes);
                    return Json(new { success = true, lotNumber = pLot, weight = pWeight, material = pMaterial });
                }

                // confirm=true → insert using supplied values
                _scheduleService.ReceivePowder(pdf.FileName, bytes, lot, weight, material);
                return Json(new { success = true, message = "Powder saved successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("GetPartSuggestions")]
        public IActionResult GetPartSuggestions(string term)
        {
            var parts = _scheduleService.GetAllSchedParts()
                         .Select(p => p.MasterId?.ToUpper())
                         .Where(p => !string.IsNullOrWhiteSpace(p) && p.StartsWith(term?.ToUpper() ?? ""))
                         .Distinct()
                         .Take(10)  // limit results
                         .ToList();

            return Json(parts);
        }
        [HttpGet("GetMaterialSuggestions")]
        public IActionResult GetMaterialSuggestions(string part, string comp, string term)
        {
            Console.WriteLine($"Incoming: part={part}, comp='{comp}', term={term}");

            // Grab ALL matching rows for that part/component
            var matches = _scheduleService.GetAllSchedParts()
                .Where(p =>
                    p.MasterId.Equals(part, StringComparison.OrdinalIgnoreCase) &&
                    ((p.Component ?? "") == (comp ?? "")))
                .ToList();

            Console.WriteLine($"Found {matches.Count} rows in sched table:");
            foreach (var m in matches)
            {
                Console.WriteLine($" --> comp:'{m.Component}', mat:'{m.MaterialCode}'");
            }

            var suggestions = matches
                .Select(p => p.MaterialCode)
                .Where(m => !string.IsNullOrWhiteSpace(m) &&
                            m.StartsWith(term, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .Take(10)
                .ToList();

            Console.WriteLine("Suggestions being returned: " + JsonSerializer.Serialize(suggestions));
            return Json(suggestions);
        }



        [HttpGet("GetPowderList")]
        public IActionResult GetPowderList()
        {
            var list = _scheduleService.GetPowderMixHistory();
            return Json(list);
        }



    }
}
