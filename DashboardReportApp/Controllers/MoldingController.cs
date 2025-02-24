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
    }
}
