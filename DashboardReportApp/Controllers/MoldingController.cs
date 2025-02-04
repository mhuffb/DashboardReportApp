using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class MoldingController : Controller
    {
        private readonly MoldingService _pressDataService;

        public MoldingController(MoldingService pressDataService)
        {
            _pressDataService = pressDataService;
        }

        [HttpGet]
        public IActionResult Index(string searchTerm)
        {
            var viewModel = _pressDataService.GetFilteredData(searchTerm, null, false);
            return View(viewModel);
        }
    }
}
