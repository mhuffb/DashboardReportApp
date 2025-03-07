using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")] // Set your password here

    [Route("Schedule")]
    public class ScheduleController : Controller
    {
        private readonly ScheduleService _scheduleService;

        public ScheduleController(ScheduleService scheduleService)
        {
            _scheduleService = scheduleService;
        }

        public IActionResult Index(string masterId = null, int quantity = 0)
        {
            var viewModel = new ScheduleModel
            {
                AllComponents = new List<SintergyComponent>(),
                AllParts = _scheduleService.GetAllSchedParts()
            };

            if (!string.IsNullOrEmpty(masterId) && quantity > 0)
            {
                viewModel.AllComponents = _scheduleService.GetComponentsForMasterId(masterId.ToUpper(), quantity);
            }

            return View(viewModel);
        }

        [HttpPost("ScheduleComponents")]
        public IActionResult ScheduleComponents(ScheduleModel viewModel)
        {
            try
            {
                if (viewModel.AllComponents == null || !viewModel.AllComponents.Any())
                {
                    TempData["Error"] = "No components found to schedule.";
                    return RedirectToAction("Index");
                }

                if (string.IsNullOrEmpty(viewModel.AllComponents.FirstOrDefault()?.MasterId))
                {
                    TempData["Error"] = "An error occurred while scheduling: Part number (MasterId) cannot be null.";
                    return RedirectToAction("Index");
                }

                _scheduleService.ScheduleComponents(viewModel);
                TempData["Success"] = "Parts and components successfully scheduled!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"An error occurred while scheduling: {ex.Message}";
            }

            return RedirectToAction("Index");
        }

        [HttpPost("UpdatePart")]
        public IActionResult UpdatePart(SintergyComponent updatedPart)
        {
            try
            {
                _scheduleService.UpdatePart(updatedPart);
                TempData["Success"] = "Part updated successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"An error occurred while updating part: {ex.Message}";
            }
            return RedirectToAction("Index");
        }
        [HttpGet("GetComponents")]
        public IActionResult GetComponents(string part, int quantity)
        {
            // Log parameters
            Console.WriteLine($"GetComponents called with part: {part}, quantity: {quantity}");
            var components = _scheduleService.GetComponentsForMasterId(part, quantity);
            Console.WriteLine($"Components count: {components.Count}");

            // Build a new model with these components.
            var model = new ScheduleModel
            {
                AllComponents = components
            };

            // Return the partial view with the model.
            return PartialView("_ComponentsPartial", model);
        }

    }
}
