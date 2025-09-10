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

        public async Task<IActionResult> Index(string search, int page = 1, int pageSize = 50, string sort = "id", string dir = "DESC")
        {
            var operators = await _service.GetOperatorsAsync();
            var machines = await _service.GetEquipmentAsync();
            var scheduleItems = await _service.GetAvailableScheduleItemsAsync();

            var (pageItems, total) = await _service.GetPagedRecordsAsync(page, pageSize, sort, dir, search);

            var vm = new SecondarySetupLogViewModel
            {
                PageItems = pageItems,
                Page = page,
                PageSize = pageSize,
                Total = total,
                Search = search,
                Sort = sort,
                Dir = dir,
                Operators = operators,
                Machines = machines,
                ScheduleItems = scheduleItems
            };

            return View(vm);
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
