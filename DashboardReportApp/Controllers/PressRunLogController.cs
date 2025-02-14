using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    public class PressRunLogController : Controller
    {
        private readonly PressRunLogService _pressRunLogService;

        public PressRunLogController(PressRunLogService service)
        {
            _pressRunLogService = service;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewData["Operators"] = await _pressRunLogService.GetOperatorsAsync();

            // openParts => for the Start Molding form
            var openParts = await _pressRunLogService.GetOpenPartsWithRunsAndMachinesAsync();
            ViewData["OpenParts"] = openParts ?? new Dictionary<(string, string), string>();

            var openRuns = await _pressRunLogService.GetLoggedInRunsAsync();
            var allRuns = await _pressRunLogService.GetAllRunsAsync();

            ViewBag.OpenRuns = openRuns;
            return View(allRuns); // the Index.cshtml
        }

        // ==================== LOGIN =====================
        // GET /PressRunLog/LoadLoginModal?machine=xxx
        [HttpGet]
        public async Task<IActionResult> LoadLoginModal(string machine)
        {
            int? deviceCount = await _pressRunLogService.TryGetDeviceCountOrNull(machine);
            ViewBag.Machine = machine;
            ViewBag.DeviceCount = deviceCount ?? 0;

            Console.WriteLine("DeviceCount: " + deviceCount);
            Console.WriteLine("Machine: " + machine);
            return PartialView("_LoginCountModal");
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmLogin(string operatorName, string part,
                                              string machine, string runNumber,
                                              int finalCount)
        {
            var formModel = new PressRunLogModel
            {
                Operator = operatorName,
                Part = part,
                Machine = machine,
                Run = runNumber,
                StartDateTime = DateTime.Now
            };
            await _pressRunLogService.HandleLoginWithCountAsync(formModel, finalCount);
            return RedirectToAction("Index");
        }

        // ==================== START SKID =====================
        // Updated action name and parameter names to match the modal form
        [HttpPost]
        public async Task<IActionResult> StartSkid(int runId, string run, string part,
                                            string machine, string operatorName,
                                            int skidcount)
        {
            await _pressRunLogService.HandleStartSkidAsync(runId, run, part, operatorName, machine, skidcount);
            return RedirectToAction("Index");
        }

        // ==================== LOGOUT =====================
        // GET /PressRunLog/LoadLogoutModal => user sees device count => typed final count
        [HttpGet]
        public async Task<IActionResult> LoadLogoutModal(int runId, string machine)
        {
            int? deviceCount = await _pressRunLogService.TryGetDeviceCountOrNull(machine);
            ViewBag.RunId = runId;
            ViewBag.Machine = machine;
            ViewBag.DeviceCount = deviceCount ?? 0;
            return PartialView("_LogoutCountModal");
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmLogout(int runId, int finalCount, int scrap, string notes)
        {
            // Update the main run record with endDateTime, pcsEnd, scrap, and notes.
            await _pressRunLogService.HandleLogoutAsync(runId, finalCount, scrap, notes);
            return RedirectToAction("Index");
        }

        // ==================== END RUN =====================
        // GET /PressRunLog/LoadEndRunModal?runId=xxx&machine=xxx => partial
        [HttpGet]
        public async Task<IActionResult> LoadEndRunModal(int runId, string machine)
        {
            int? deviceCount = await _pressRunLogService.TryGetDeviceCountOrNull(machine);
            ViewBag.RunId = runId;
            ViewBag.Machine = machine;
            ViewBag.DeviceCount = deviceCount ?? 0;
            return PartialView("_EndRunCountModal");
        }
        [HttpGet]
        public async Task<IActionResult> ApiGetDeviceCount(string machine)
        {
            // 1) Call your service to get device count or null
            int? count = await _pressRunLogService.TryGetDeviceCountOrNull(machine);
            // 2) Return JSON
            return Json(new { deviceCount = count ?? 0 });
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmEndRun(int runId, int finalCount, int scrap, string notes)
        {
            await _pressRunLogService.HandleEndRunAsync(runId, finalCount, scrap, notes);
            return RedirectToAction("Index");
        }
    }
}
