using Microsoft.AspNetCore.Mvc;
using DashboardReportApp.Services;
using System.Threading.Tasks;
using DashboardReportApp.Models;

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
        public async Task<IActionResult> Index()
            {
            ViewBag.Operators = await _secondaryRunLogService.GetOperatorsAsync();
            ViewBag.Machines = await _secondaryRunLogService.GetMachinesAsync();
            ViewBag.OpenRuns = await _secondaryRunLogService.GetLoggedInRunsAsync();
            ViewBag.AvailableParts = await _secondaryRunLogService.GetAvailableParts();

            // Fetch all runs 
            var allRuns = await _secondaryRunLogService.GetAllRunsAsync();

            
            // Return all runs as the model for React table
            return View(allRuns);
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
