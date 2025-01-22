using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class SecondarySetupLogController : Controller
    {
        private readonly ISecondarySetupLogService _service;

        public SecondarySetupLogController(ISecondarySetupLogService service)
        {
            _service = service;
        }

        public async Task<IActionResult> Index()
        {
            var model = new SecondarySetupLogViewModel
            {
                Operators = await _service.GetOperatorsAsync(),
                Equipment = await _service.GetEquipmentAsync()
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> CreateSetup(SecondarySetupLogViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Look up the part number using the run number
                int? runNumber = int.TryParse(model.Run, out var run) ? run : null;
                string partNumber = await _service.LookupPartNumberAsync(runNumber);

                if (string.IsNullOrWhiteSpace(partNumber))
                {
                    TempData["Error"] = "Failed to find the part number for the given run.";
                    return RedirectToAction("Index");
                }

                // Save the setup
                await _service.AddSetupAsync(
                    model.Operator,
                    partNumber, // Use the looked-up part number
                    model.Machine,
                    model.Run,
                    model.Pcs,
                    model.ScrapMach,
                    model.ScrapNonMach,
                    model.Notes,
                    model.SetupHours
                );

                TempData["Message"] = "Setup added successfully!";
            }
            else
            {
                TempData["Error"] = "Failed to add setup. Please check your inputs.";
            }

            return RedirectToAction("Index");
        }

    }


}
