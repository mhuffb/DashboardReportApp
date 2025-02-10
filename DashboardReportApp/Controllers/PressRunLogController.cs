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
            // Load supporting data for dropdowns
            ViewData["Operators"] = await _pressRunLogService.GetOperatorsAsync();
            ViewData["Equipment"] = await _pressRunLogService.GetEquipmentAsync();

            // Load open parts/runs for the login form
            var openParts = await _pressRunLogService.GetOpenPartsWithRunsAndMachinesAsync();
            ViewData["OpenParts"] = openParts ?? new Dictionary<(string, string), string>();

            // "Open Runs" = runs with open=1
            var openRuns = await _pressRunLogService.GetLoggedInRunsAsync();
            // "All Runs" = entire pressrun table
            var allRuns = await _pressRunLogService.GetAllRunsAsync();

            // Put open runs in ViewBag
            ViewBag.OpenRuns = openRuns;

            // Return all runs as the model (for React table)
            return View(allRuns);
        }

        [HttpPost]
        public async Task<IActionResult> Login(string operatorName, string part, string machine)
        {
            if (string.IsNullOrEmpty(operatorName) || string.IsNullOrEmpty(part) || string.IsNullOrEmpty(machine))
            {
                ModelState.AddModelError("", "All fields are required for login.");
                return RedirectToAction("Index");
            }

            var formModel = new PressRunLogModel
            {
                Operator = operatorName,
                Part = part,
                Machine = machine,
                StartDateTime = DateTime.Now
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

            var formModel = new PressRunLogModel
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
