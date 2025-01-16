using Microsoft.AspNetCore.Mvc;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    public class PressRunLogController : Controller
    {
        private readonly PressRunLogService _pressRunLogService;

        public PressRunLogController(PressRunLogService pressRunLogService)
        {
            _pressRunLogService = pressRunLogService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Get the ViewModel with equipment, operators, and logged-in runs
            var viewModel = await _pressRunLogService.GetPressRunLogViewModelAsync();
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Login(PressRunLogFormModel formModel)
        {
            if (!ModelState.IsValid)
            {
                // Reload the ViewModel to include dropdown data and show validation errors
                var viewModel = await _pressRunLogService.GetPressRunLogViewModelAsync();
                viewModel.FormModel = formModel;
                return View("Index", viewModel);
            }

            // Handle login logic
            await _pressRunLogService.HandleLoginAsync(formModel);

            // Redirect to Index to refresh the list of logged-in runs
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> Logout(PressRunLogFormModel formModel)
        {
            if (!ModelState.IsValid)
            {
                // Reload the ViewModel to include dropdown data and show validation errors
                var viewModel = await _pressRunLogService.GetPressRunLogViewModelAsync();
                viewModel.FormModel = formModel;
                return View("Index", viewModel);
            }

            // Handle logout logic
            await _pressRunLogService.HandleLogoutAsync(formModel);

            // Redirect to Index to refresh the list of logged-in runs
            return RedirectToAction("Index");
        }
    }
}
