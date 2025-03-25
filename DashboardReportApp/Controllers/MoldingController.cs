using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class MoldingController : Controller
    {
        private readonly MoldingService _moldingService;
        private readonly SharedService _sharedService;

        public MoldingController(MoldingService moldingService, SharedService sharedService)
        {
            _moldingService = moldingService;
            _sharedService = sharedService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var viewModel = _moldingService.GetData();
            return View(viewModel);
        }
        [HttpGet]
        public async Task<IActionResult> ApiGetAllMachineCounts()
        {
            // Returns JSON: { "1": 123, "2": 456, ... }
            Dictionary<string, int?> allCounts = await _moldingService.GetAllMachineCountsAsync();
            return Json(allCounts);
        }

        [HttpGet]
        public async Task<IActionResult> ApiGetLatestPart([FromQuery] string machine)
        {
            if (string.IsNullOrWhiteSpace(machine))
            {
                return BadRequest("Machine parameter is required.");
            }

            // Assuming MoldingService wraps SharedService; otherwise, adjust to get a SharedService instance.
            string partName = await _sharedService.GetLatestProlinkPartForMachineAsync(machine);

            if (string.IsNullOrEmpty(partName))
            {
                return NotFound("No part found for the specified machine in the last hour.");
            }

            return Json(new { partName });
        }

    }

}
