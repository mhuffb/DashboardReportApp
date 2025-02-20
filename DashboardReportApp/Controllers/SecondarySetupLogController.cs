using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class SecondarySetupLogController : Controller
    {
        private readonly SecondarySetupLogService _service;

        public SecondarySetupLogController(SecondarySetupLogService service)
        {
            _service = service;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Operators = await _service.GetOperatorsAsync();
            ViewBag.Machines = await _service.GetEquipmentAsync();

            // This call now returns List<SecondarySetupLogModel>
            var allRuns = await _service.GetAllRecords();

            return View(allRuns); // Pass the typed list to the view
        }


        [HttpPost]
        public async Task<IActionResult> CreateSetup(SecondarySetupLogModel model)
        {
            if (ModelState.IsValid)
            {
                int? runNumber = int.TryParse(model.Run, out var run) ? run : null;
                string partNumber = await _service.LookupPartNumberAsync(runNumber);

                if (string.IsNullOrWhiteSpace(partNumber))
                {
                    TempData["Error"] = "Failed to find the part number for the given run.";
                    return RedirectToAction("Index");
                }

                await _service.AddSetupAsync(
                    model.Operator,
                    model.Op,
                    partNumber,
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
