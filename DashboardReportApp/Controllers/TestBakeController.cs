using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    public class TestBakeController : Controller
    {
        private readonly ITestBakeService _service;

        public TestBakeController(ITestBakeService service)
        {
            _service = service;
        }

        [HttpGet]
        public IActionResult Initial()
        {
            var vm = new TestBakeViewModel();
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Initial(TestBakeViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.SearchProductionNumber) &&
                string.IsNullOrWhiteSpace(model.SearchRunNumber))
            {
                ModelState.AddModelError("", "Scan a production number or run number.");
                return View(model);
            }

            var vm = await _service.LoadInitialAsync(
                model.SearchProductionNumber,
                model.SearchRunNumber,
                model.SearchDepartment,
                model.SearchTestType,
                model.SearchReason);

            if (!string.IsNullOrEmpty(vm.ErrorMessage))
            {
                ModelState.AddModelError("", vm.ErrorMessage);
            }

            return View(vm);
        }
    }
}
