using Microsoft.AspNetCore.Mvc;
using DashboardReportApp.Services;
using System.Threading.Tasks;
using DashboardReportApp.Models;

namespace DashboardReportApp.Controllers
{
    public class SecondaryRunLogController : Controller
    {
        private readonly ISecondaryRunLogService _secondaryRunLogService;

        public SecondaryRunLogController(ISecondaryRunLogService secondaryRunLogService)
        {
            _secondaryRunLogService = secondaryRunLogService;
        }

        public async Task<IActionResult> Index()
        {
            var operators = await _secondaryRunLogService.GetOperatorsAsync();
            var machines = await _secondaryRunLogService.GetMachinesAsync();
            var openRuns = await _secondaryRunLogService.GetActiveRunsAsync();

            var viewModel = new SecondaryRunLogViewModel
            {
                Operators = operators,
                Machines = machines,
                OpenRuns = openRuns
            };

            return View(viewModel);
        }


        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string operatorName, string machine, string runNumber)
        {
            await _secondaryRunLogService.LoginAsync(operatorName, machine, runNumber);
            return RedirectToAction("Index");
        }

        public IActionResult Logout()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Logout(int pcs, int scrapMach, int scrapNonMach, string notes, int selectedRunId)
        {
            await _secondaryRunLogService.LogoutAsync(pcs, scrapMach, scrapNonMach, notes, selectedRunId);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> OpenRuns()
        {
            var openRuns = await _secondaryRunLogService.GetActiveRunsAsync();
            return View(openRuns);
        }

        public IActionResult CreateRun()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateRun(string runNumber, string operatorName, string machine, string op)
        {
            await _secondaryRunLogService.LoginAsync(operatorName, machine, runNumber, op);
            return RedirectToAction("Index");
        }


        [HttpPost]
        public async Task<IActionResult> CloseRun(int id, int pcs, int scrapMach, int scrapNonMach, string notes)
        {
            await _secondaryRunLogService.LogoutAsync(pcs, scrapMach, scrapNonMach, notes, id);
            return RedirectToAction("Index");
        }


        public async Task<IActionResult> CloseRun(int id)
        {
            var run = await _secondaryRunLogService.GetRunByIdAsync(id); // Fetch run details
            return View(run);
        }


    }
}
