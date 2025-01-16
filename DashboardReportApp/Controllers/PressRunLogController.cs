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
            var viewModel = await _pressRunLogService.GetPressRunLogViewModelAsync();
            return View(viewModel);
        }



        [HttpPost]
        public async Task<IActionResult> Login(string operatorName, string part, string machine)
        {
            if (string.IsNullOrEmpty(operatorName) || string.IsNullOrEmpty(part) || string.IsNullOrEmpty(machine))
            {
                ModelState.AddModelError("", "All fields are required for login.");
                return RedirectToAction("Index");
            }

            var formModel = new PressRunLogFormModel
            {
                Operator = operatorName,
                Part = part,
                Machine = machine,
                StartDateTime = DateTime.Now // Automatically set the current time
            };

            await _pressRunLogService.HandleLoginAsync(formModel);
            return RedirectToAction("Index");
        }




        [HttpPost]
        public async Task<IActionResult> Logout(string part, DateTime startDateTime, int scrap, string notes, DateTime endDateTime)
        {
            if (string.IsNullOrEmpty(part) || startDateTime == default || endDateTime == default)
            {
                ModelState.AddModelError("", "Invalid logout data. Ensure all required fields are provided.");
                return RedirectToAction("Index");
            }

            var formModel = new PressRunLogFormModel
            {
                Part = part,
                StartDateTime = startDateTime,
                Scrap = scrap,
                Notes = notes,
                EndDateTime = endDateTime
            };

            await _pressRunLogService.HandleLogoutAsync(formModel);
            return RedirectToAction("Index");
        }



    }
}
