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

        public PressSetupController(PressSetupService pressSetupService)
        {
            _pressSetupService = pressSetupService;
        }

        [HttpGet]
        public IActionResult Index(string part, string operatorName, string machine, string setupComplete,
                                   string assistanceRequired, string search, string startDate, string endDate,
                                   string sortBy, string sortOrder = "desc")
        {
            ViewData["Title"] = "Press Setup";
            ViewData["Operators"] = _pressSetupService.GetOperators();
            ViewData["Machines"] = _pressSetupService.GetEquipment();
            ViewData["Trainers"] = _pressSetupService.GetTrainers();
            ViewData["SortOrder"] = sortOrder == "asc" ? "desc" : "asc";
            var scheduledParts = _pressSetupService.GetScheduledParts();
            ViewData["Parts"] = scheduledParts;
            var records = _pressSetupService.GetAllRecords();

            return View(records);
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
                                                string assistanceRequired, string assistedBy, string setupComplete, string notes, string runNumber)
        {
            try
            {
                await _pressSetupService.LogoutAsync(partNumber, startDateTime, difficulty, assistanceRequired, assistedBy, setupComplete, notes, runNumber);
                TempData["Message"] = "Logout successfully recorded!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"An error occurred: {ex.Message}";
            }
            return RedirectToAction("Index");
        }

       

    }
}
