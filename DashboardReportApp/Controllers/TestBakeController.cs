using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    public class TestBakeController : Controller
    {
        private readonly ITestBakeService _service;

        private readonly SharedService _sharedService;

        public TestBakeController(ITestBakeService service, SharedService sharedService)
        {
            _service = service;
            _sharedService = sharedService;              // 👈 store it
        }

        [HttpGet]
        public async Task<IActionResult> Index(int page = 1, int pageSize = 25)
        {
            var vm = new TestBakeViewModel
            {
                Page = page,
                PageSize = pageSize
            };

            vm.ActiveLogins = await _service.GetActiveLoginsAsync();
            vm.TotalCount = await _service.GetHeaderCountAsync();
            vm.TotalPages = (int)Math.Ceiling((double)vm.TotalCount / vm.PageSize);
            vm.HeaderHistory = await _service.GetRecentHeadersAsync(page, pageSize);
            ViewData["Operators"] = _sharedService.GetFormattedOperators();
            ViewData["Furnaces"] = _sharedService.GetFurnaces();


            return View(vm);   // now resolves to Views/TestBake/Index.cshtml
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(TestBakeViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.SearchProductionNumber) &&
                string.IsNullOrWhiteSpace(model.SearchRunNumber))
            {
                ModelState.AddModelError("", "Scan a production number or run number.");
                model.ActiveLogins = await _service.GetActiveLoginsAsync();
                ViewData["Operators"] = _sharedService.GetFormattedOperators();
                ViewData["Furnaces"] = _sharedService.GetFurnaces();
                return View(model);
            }

            var vm = await _service.LoadInitialAsync(
                model.SearchProductionNumber,
                model.SearchRunNumber,
                model.SearchTestType,
                model.SearchReason,
                model.TestBakeStartTime,
                model.TestBakeEndTime,
                model.HeaderId);

            if (!string.IsNullOrEmpty(vm.ErrorMessage))
            {
                ModelState.AddModelError("", vm.ErrorMessage);
            }

            vm.ActiveLogins = await _service.GetActiveLoginsAsync();
            vm.Page = 1;
            vm.PageSize = 25;
            vm.TotalCount = await _service.GetHeaderCountAsync();
            vm.TotalPages = (int)Math.Ceiling((double)vm.TotalCount / vm.PageSize);
            vm.HeaderHistory = await _service.GetRecentHeadersAsync(vm.Page, vm.PageSize);
            ViewData["Operators"] = _sharedService.GetFormattedOperators();
            ViewData["Furnaces"] = _sharedService.GetFurnaces();
            return View(vm);
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PlaceTestBake(
     string Operator,
     string Furnace,
     string ProductionNumber,
     string RunNumber,
     string Part,
     string? Component,
     string TestType,
     string Reason)
        {
            if (string.IsNullOrWhiteSpace(Operator))
                ModelState.AddModelError("", "Operator is required.");

            if (string.IsNullOrWhiteSpace(Furnace))
                ModelState.AddModelError("", "Furnace is required.");

            if (string.IsNullOrWhiteSpace(ProductionNumber) &&
                string.IsNullOrWhiteSpace(RunNumber))
                ModelState.AddModelError("", "Scan or type a Production # or Run #.");

            if (!ModelState.IsValid)
            {
                var vmError = new TestBakeViewModel
                {
                    ActiveLogins = await _service.GetActiveLoginsAsync()
                };
                vmError.Page = 1;
                vmError.PageSize = 25;
                vmError.TotalCount = await _service.GetHeaderCountAsync();
                vmError.TotalPages = (int)Math.Ceiling((double)vmError.TotalCount / vmError.PageSize);
                vmError.HeaderHistory = await _service.GetRecentHeadersAsync(vmError.Page, vmError.PageSize);
                ViewData["Operators"] = _sharedService.GetFormattedOperators();
                ViewData["Furnaces"] = _sharedService.GetFurnaces();
                return View("Index", vmError);
            }

            var loginRow = new TestBakeLoginRow
            {
                Operator = Operator,
                Furnace = Furnace,
                ProductionNumber = string.IsNullOrWhiteSpace(ProductionNumber) ? null : ProductionNumber,
                RunNumber = string.IsNullOrWhiteSpace(RunNumber) ? null : RunNumber,
                Part = string.IsNullOrWhiteSpace(Part) ? null : Part,
                Component = string.IsNullOrWhiteSpace(Component) ? null : Component,
                TestType = TestType,
                Reason = Reason,
                StartTime = DateTime.Now
            };

            await _service.PlaceTestBakeAsync(loginRow);

            var vm = new TestBakeViewModel
            {
                ActiveLogins = await _service.GetActiveLoginsAsync()
            };
            vm.Page = 1;
            vm.PageSize = 25;
            vm.TotalCount = await _service.GetHeaderCountAsync();
            vm.TotalPages = (int)Math.Ceiling((double)vm.TotalCount / vm.PageSize);
            vm.HeaderHistory = await _service.GetRecentHeadersAsync(vm.Page, vm.PageSize);

            TempData["Success"] = "Test bake placed on furnace.";
            ViewData["Operators"] = _sharedService.GetFormattedOperators();
            ViewData["Furnaces"] = _sharedService.GetFurnaces();
            return View("Index", vm);
        }


        [HttpGet]
        public async Task<IActionResult> TestBakePdf(int headerId)
        {
            try
            {
                var bytes = await _service.GetPdfFromDiskAsync(headerId);
                var fileName = $"TestBake_{headerId}.pdf";
                return File(bytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                // If you want, you can log ex here
                TempData["Error"] = $"Error loading PDF: {ex.Message}";

                var vm = new TestBakeViewModel
                {
                    ActiveLogins = await _service.GetActiveLoginsAsync()
                };
                vm.Page = 1;
                vm.PageSize = 25;
                vm.TotalCount = await _service.GetHeaderCountAsync();
                vm.TotalPages = (int)Math.Ceiling((double)vm.TotalCount / vm.PageSize);
                vm.HeaderHistory = await _service.GetRecentHeadersAsync(vm.Page, vm.PageSize);
                ViewData["Operators"] = _sharedService.GetFormattedOperators();
                ViewData["Furnaces"] = _sharedService.GetFurnaces();
                return View("Index", vm);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PlaceTestBakeOnFurnace(
    string Operator,
    string Furnace,
    string ProductionNumber,
    string RunNumber,
    string Part,
    string? Component,
    string TestType,
    string Reason)
        {
            if (string.IsNullOrWhiteSpace(Operator))
            {
                ModelState.AddModelError("", "Operator is required.");
            }
            if (string.IsNullOrWhiteSpace(Furnace))
            {
                ModelState.AddModelError("", "Furnace is required.");
            }

            if (!ModelState.IsValid)
            {
                // reload basic VM (active logins + header history) if validation fails
                var vm = new TestBakeViewModel
                {
                    ActiveLogins = await _service.GetActiveLoginsAsync()
                };
                vm.Page = 1;
                vm.PageSize = 25;
                vm.TotalCount = await _service.GetHeaderCountAsync();
                vm.TotalPages = (int)Math.Ceiling((double)vm.TotalCount / vm.PageSize);
                vm.HeaderHistory = await _service.GetRecentHeadersAsync(vm.Page, vm.PageSize);
                ViewData["Operators"] = _sharedService.GetFormattedOperators();
                ViewData["Furnaces"] = _sharedService.GetFurnaces();
                return View("Index", vm);
            }

            var login = new TestBakeLoginRow
            {
                Operator = Operator,
                StartTime = DateTime.Now,
                Furnace = Furnace,
                ProductionNumber = string.IsNullOrWhiteSpace(ProductionNumber) ? null : ProductionNumber,
                RunNumber = string.IsNullOrWhiteSpace(RunNumber) ? null : RunNumber,
                Part = string.IsNullOrWhiteSpace(Part) ? null : Part,
                Component = string.IsNullOrWhiteSpace(Component) ? null : Component,
                TestType = string.IsNullOrWhiteSpace(TestType) ? null : TestType,
                Reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason
            };

            await _service.PlaceTestBakeAsync(login);
            ViewData["Operators"] = _sharedService.GetFormattedOperators();
            ViewData["Furnaces"] = _sharedService.GetFurnaces();
            return RedirectToAction(nameof(Index));
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinishTestBake(int loginId)
        {
            try
            {
                var headerId = await _service.FinishTestBakeAsync(loginId);
                TempData["Success"] = $"Finished test bake. PDF #{headerId} created.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error finishing test bake: {ex.Message}";
            }

            var vm = new TestBakeViewModel
            {
                ActiveLogins = await _service.GetActiveLoginsAsync()
            };
            vm.Page = 1;
            vm.PageSize = 25;
            vm.TotalCount = await _service.GetHeaderCountAsync();
            vm.TotalPages = (int)Math.Ceiling((double)vm.TotalCount / vm.PageSize);
            vm.HeaderHistory = await _service.GetRecentHeadersAsync(vm.Page, vm.PageSize);
            ViewData["Operators"] = _sharedService.GetFormattedOperators();
            ViewData["Furnaces"] = _sharedService.GetFurnaces();
            return View("Index", vm);
        }

    }
}
