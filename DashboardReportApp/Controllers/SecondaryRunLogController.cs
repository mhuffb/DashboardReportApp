using Microsoft.AspNetCore.Mvc;
using DashboardReportApp.Services;
using System.Threading.Tasks;
using DashboardReportApp.Models;
using static DashboardReportApp.Models.SecondaryRunLogModel;

namespace DashboardReportApp.Controllers
{
    public class SecondaryRunLogController : Controller
    {
        private readonly SecondaryRunLogService _secondaryRunLogService;

        public SecondaryRunLogController(SecondaryRunLogService secondaryRunLogService)
        {
            _secondaryRunLogService = secondaryRunLogService;
        }


        [HttpGet]
        public async Task<IActionResult> Index(string search, int page = 1, int pageSize = 50, string sort = "id", string dir = "DESC")
        {
            // Keep these ViewBags for the top two tables and modal
            ViewBag.Operators = await _secondaryRunLogService.GetOperatorsAsync();
            ViewBag.Machines = await _secondaryRunLogService.GetMachinesAsync();
            ViewBag.OpenRuns = await _secondaryRunLogService.GetLoggedInRunsAsync();
            ViewBag.AvailableParts = await _secondaryRunLogService.GetAvailableParts();

            var (pageItems, total) = await _secondaryRunLogService.GetPagedRunsAsync(page, pageSize, sort, dir, search);

            var vm = new SecondaryRunLogViewModel
            {
                PageItems = pageItems,
                Page = page,
                PageSize = pageSize,
                Total = total,
                Search = search,
                Sort = sort,
                Dir = dir
            };

            return View(vm);
        }



        [HttpPost]
        public async Task<IActionResult> Login(SecondaryRunLogModel model)
        {
            await _secondaryRunLogService.HandleLoginAsync(model);
            return RedirectToAction("Index");
        }


        [HttpPost]
        public async Task<IActionResult> Logout(int id, int pcs, int scrapMach, int scrapNonMach, string notes)
        {
            await _secondaryRunLogService.HandleLogoutAsync(pcs, scrapMach, scrapNonMach, notes, id);

            // Detect AJAX (SweetAlert expects JSON)
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                          Request.Headers["Accept"].ToString().Contains("application/json");

            if (isAjax)
            {
                return Json(new { ok = true, message = "Logged out successfully." });
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> EndRun(
    int id,
    int pcs,
    int scrapMach,
    int scrapNonMach,
    string notes,
    string prodNumber,
    string part,
    bool orderComplete)
        {
            // Update the secondaryrun table (using your existing logout routine)
            await _secondaryRunLogService.HandleLogoutAsync(pcs, scrapMach, scrapNonMach, notes, id);

            await _secondaryRunLogService.UpdateSecondarySetupAsync(prodNumber, part);
            // Update the schedule table based on whether the order is complete.
            await _secondaryRunLogService.UpdateScheduleAsync(prodNumber, part, orderComplete);

            return RedirectToAction("Index");
        }



    }
}
