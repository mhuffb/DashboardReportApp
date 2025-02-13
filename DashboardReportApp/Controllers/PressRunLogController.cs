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

        public PressRunLogController(PressRunLogService pressRunLogService)
        {
            _pressRunLogService = pressRunLogService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewData["Operators"] = await _pressRunLogService.GetOperatorsAsync();
            ViewData["Equipment"] = await _pressRunLogService.GetEquipmentAsync();

            var openParts = await _pressRunLogService.GetOpenPartsWithRunsAndMachinesAsync();
            ViewData["OpenParts"] = openParts ?? new Dictionary<(string, string), string>();

            var openRuns = await _pressRunLogService.GetLoggedInRunsAsync();
            var allRuns = await _pressRunLogService.GetAllRunsAsync();

            ViewBag.OpenRuns = openRuns;
            return View(allRuns);
        }

        [HttpPost]
        public async Task<IActionResult> Login(
     string operatorName,
     string part,
     string machine,
     string runNumber)
        {
            if (string.IsNullOrEmpty(operatorName) ||
                string.IsNullOrEmpty(part) ||
                string.IsNullOrEmpty(machine))
            {
                ModelState.AddModelError("", "All fields are required for login.");
                return RedirectToAction("Index");
            }

            var formModel = new PressRunLogModel
            {
                Operator = operatorName,
                Part = part,
                Machine = machine,
                Run = runNumber,
                StartDateTime = DateTime.Now
            };

            // Insert row and get the new ID
            int newRunId = await _pressRunLogService.HandleLoginAsync(formModel);

            // Store that ID in TempData or ViewBag so you can display it or pass to the next request.
            TempData["NewRunId"] = newRunId;

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> Logout(
     int runId,
     int scrap,
     string notes)
        {
            // If runId is not found or 0, handle error
            if (runId <= 0)
            {
                ModelState.AddModelError("", "Invalid runId for logout.");
                return RedirectToAction("Index");
            }

            await _pressRunLogService.HandleLogoutAsync(runId, scrap, notes);
            return RedirectToAction("Index");
        }



        // Skid logic: Single “Start Skid” action that does both first skid and next skid.
        [HttpPost]
        public async Task<IActionResult> StartSkid(
            int runId,
            string run,
            string part,
            string operatorName,
            string machine,
            int skidcount)
        {
            await _pressRunLogService.HandleStartSkidAsync(
                runId, run, part, operatorName, machine, skidcount);
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> EndRun(int runId, string part, int scrap, string notes)
        {
            if (runId <= 0)
            {
                ModelState.AddModelError("", "Invalid runId for End Run.");
                return RedirectToAction("Index");
            }

            await _pressRunLogService.HandleEndRunAsync(runId, part, scrap, notes);
            return RedirectToAction("Index");
        }



        // End skid by ID (optional).
        [HttpPost]
        public async Task<IActionResult> EndSkid(int skidRecordId)
        {
            await _pressRunLogService.HandleEndSkidAsync(skidRecordId);
            return RedirectToAction("Index");
        }
    }
}
