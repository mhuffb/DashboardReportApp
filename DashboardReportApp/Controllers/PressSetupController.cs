using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DashboardReportApp.Services;
using DashboardReportApp.Models;

namespace DashboardReportApp.Controllers
{
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

            var records = _pressSetupService.GetAllRecords(part, operatorName, machine, setupComplete, assistanceRequired, search, startDate, endDate, sortBy, sortOrder);

            return View(records);
        }

        [HttpPost]
        public async Task<IActionResult> Login(string partNumber, string operatorName, string machine)
        {
            try
            {
                await _pressSetupService.LoginAsync(partNumber, operatorName, machine);
                TempData["Message"] = "Login successfully recorded!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"An error occurred: {ex.Message}";
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> Logout(string partNumber, DateTime startDateTime, string difficulty,
                                                string assistanceRequired, string assistedBy, string setupComplete, string notes)
        {
            try
            {
                await _pressSetupService.LogoutAsync(partNumber, startDateTime, difficulty, assistanceRequired, assistedBy, setupComplete, notes);
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
