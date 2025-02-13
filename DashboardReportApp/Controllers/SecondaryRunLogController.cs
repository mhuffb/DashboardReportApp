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
            

            // Fetch all runs (instead of open runs)
            var allRuns = await _secondaryRunLogService.GetAllRunsAsync();

            
            // Return all runs as the model for React table
            return View(allRuns);
        }
        

        [HttpPost]
        public async Task<IActionResult> Login(string runNumber, string operatorName, string machine, string op)
        {
            await _secondaryRunLogService.HandleLoginAsync(operatorName, machine, runNumber, op);
            return RedirectToAction("Index");
        }
       

        [HttpPost]
        public async Task<IActionResult> Logout(int id, int pcs, int scrapMach, int scrapNonMach, string notes)
        {
            await _secondaryRunLogService.HandleLogoutAsync(pcs, scrapMach, scrapNonMach, notes, id);
            return RedirectToAction("Index");
        }



    }
}
