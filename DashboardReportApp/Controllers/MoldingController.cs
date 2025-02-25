using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class MoldingController : Controller
    {
        private readonly MoldingService _moldingService;

        public MoldingController(MoldingService moldingService)
        {
            _moldingService = moldingService;
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
    }

}
