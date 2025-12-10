using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DashboardReportApp.Services;
using DashboardReportApp.Models;

namespace DashboardReportApp.Controllers
{
    [Route("PressSetup")]
    public class PressSetupController : Controller
    {
        private readonly PressSetupService _pressSetupService;
        private readonly SharedService _sharedService;   // 👈 add this

        public PressSetupController(PressSetupService pressSetupService, SharedService sharedService)
        {
            _pressSetupService = pressSetupService;
            _sharedService = sharedService;             // 👈 store it
        }

        [HttpGet]
        public IActionResult Index(string part, string operatorName, string machine, string setupComplete,
                                   string assistanceRequired, string search, string startDate, string endDate,
                                   string sortBy, string sortOrder = "desc")
        {
            ViewData["Title"] = "Press Setup";
            ViewData["Operators"] = _sharedService.GetFormattedOperators();

            ViewData["Machines"] = _pressSetupService.GetEquipment();
            ViewData["Trainers"] = _pressSetupService.GetTrainers();
            ViewData["Parts"] = _pressSetupService.GetScheduledParts();

            // Only need open setups for the top table:
            var openOnly = _pressSetupService
                .GetAllRecords()
                .Where(x => x.EndDateTime == null)
                .ToList();

            return View(openOnly);
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login(PressSetupLoginViewModel model)
        {
            try
            {
                await _pressSetupService.LoginAsync(model);
                TempData["Message"] = "Login successfully recorded!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"An error occurred: {ex.Message}";
            }
            return RedirectToAction("Index");
        }


        [HttpPost("Logout")]
        public async Task<IActionResult> Logout(string partNumber, DateTime startDateTime, string difficulty,
     string assistanceRequired, string assistedBy, string setupComplete, string notes, string runNumber, string machine)
        {
            // Log the machine value from the form:
            var machineFromForm = Request.Form["machine"];
            Console.WriteLine($"[Logout] Received machine parameter: '{machineFromForm}'");

            try
            {
                await _pressSetupService.LogoutAsync(partNumber, startDateTime, difficulty, assistanceRequired, assistedBy, setupComplete, notes, runNumber);
                // If setup is complete, reset the counter on the device.
                if (setupComplete == "Yes")
                {
                    await _pressSetupService.ResetPressCounterAsync(machine);
                }
                TempData["Message"] = "Logout successfully recorded!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"An error occurred: {ex.Message}";
            }
            return RedirectToAction("Index");
        }


        // PressSetupController.cs
        [HttpGet("Records")]
        public IActionResult Records(
            int page = 1,
            int pageSize = 100,
            string? search = null,
            string? sortBy = "StartDateTime",
            string sortDir = "desc",
            DateTime? startDate = null,   // NEW
            DateTime? endDate = null,     // NEW
            string? machine = null)       // NEW
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 10, 500);

            var (rows, total) = _pressSetupService.GetRecordsPage(
                page, pageSize, search, sortBy, sortDir, startDate, endDate, machine);

            return Json(new
            {
                page,
                pageSize,
                total,
                rows
            });
        }

        [HttpGet("ApiMachines")]
        public IActionResult ApiMachines()
        {
            return Json(_pressSetupService.GetEquipment());
        }

        [HttpPost("PullMaterial")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PullMaterial(long id)
        {
            try
            {
                await _pressSetupService.RefreshMaterialFromPressAsync(id);
                TempData["Message"] = "Material code and lot number pulled from press.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error pulling material: {ex.Message}";
            }
            return RedirectToAction("Index");
        }

    }
}
