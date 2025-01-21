using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace DashboardReportApp.Controllers
{
    public class ScheduleController : Controller
    {
        private readonly ScheduleService _scheduleService;

        public ScheduleController(ScheduleService scheduleService)
        {
            _scheduleService = scheduleService;
        }

        public IActionResult Index(string masterId = null, int quantity = 0)
        {
            var viewModel = new ScheduleViewModel
            {
                AllComponents = new List<SintergyComponent>(),
                OpenParts = _scheduleService.GetOpenParts()
            };

            if (!string.IsNullOrEmpty(masterId) && quantity > 0)
            {
                viewModel.AllComponents = _scheduleService.GetComponentsForMasterId(masterId.ToUpper(), quantity);
            }

            return View(viewModel);
        }

        [HttpPost]
        public IActionResult ScheduleComponents(ScheduleViewModel viewModel)
        {
            try
            {
                _scheduleService.ScheduleComponents(viewModel);
                TempData["Success"] = "Parts and components successfully scheduled!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"An error occurred while scheduling: {ex.Message}";
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult UpdateOpenParts(ScheduleViewModel viewModel)
        {
            try
            {
                _scheduleService.UpdateOpenParts(viewModel);
                TempData["Success"] = "Open parts updated successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"An error occurred while updating open parts: {ex.Message}";
            }

            return RedirectToAction("Index");
        }
    }
}
