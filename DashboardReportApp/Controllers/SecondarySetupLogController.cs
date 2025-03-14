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
            // Load the schedule items for the dropdown
            ViewBag.ScheduleItems = await _service.GetAvailableScheduleItemsAsync();

            var allRuns = await _service.GetAllRecords();
            return View(allRuns);
        }



        [HttpPost]
        public async Task<IActionResult> CreateSetup(SecondarySetupLogModel model, string ScheduleOption)
        {
            // Optionally remove model state errors for fields you update client-side
            ModelState.Remove("ProdNumber");
            ModelState.Remove("Part");
            ModelState.Remove("Run");

            if (ModelState.IsValid)
            {
                // Optionally, if ScheduleOption is still provided, you can log it:
                Console.WriteLine("ScheduleOption posted value: " + ScheduleOption);

                // At this point, model.Part and model.ProdNumber should be populated via the hidden fields.
                await _service.AddSetupAsync(model);
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
