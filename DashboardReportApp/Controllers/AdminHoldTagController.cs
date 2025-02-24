using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")] // Example password
    [Route("AdminHoldTag")]
    public class AdminHoldTagController : Controller
    {
        private readonly AdminHoldTagService _holdtagservice;

        public AdminHoldTagController(AdminHoldTagService service)
        {
            _holdtagservice = service;
        }

        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            // Get all records
            List<AdminHoldTagModel> records = await _holdtagservice.GetAllHoldRecordsAsync();

            // Get operator lists for the dropdowns
            ViewBag.IssuedByOperators = await _holdtagservice.GetIssuedByOperatorsAsync();
            ViewBag.DispositionOperators = await _holdtagservice.GetDispositionOperatorsAsync();
            ViewBag.ReworkOperators = await _holdtagservice.GetReworkOperatorsAsync();

            return View(records); // Renders the AdminHoldTag view
        }

        [HttpPost("UpdateRequest")]
        public async Task<IActionResult> UpdateRequest(AdminHoldTagModel model, IFormFile? FileUpload1, IFormFile? FileUpload2)
        {
            if (!ModelState.IsValid)
            {
                // Log validation errors for debugging
                foreach (var state in ModelState)
                {
                    foreach (var error in state.Value.Errors)
                    {
                        Console.WriteLine($"Key: {state.Key}, Error: {error.ErrorMessage}");
                    }
                }

                // Reload data for the view
                var allRecords = await _holdtagservice.GetAllHoldRecordsAsync();
                ViewBag.IssuedByOperators = await _holdtagservice.GetIssuedByOperatorsAsync();
                ViewBag.DispositionOperators = await _holdtagservice.GetDispositionOperatorsAsync();
                ViewBag.ReworkOperators = await _holdtagservice.GetReworkOperatorsAsync();

                TempData["ErrorMessage"] = "Please correct the errors and try again.";
                return View("Index", allRecords);
            }

            try
            {
                // Call the service to update the request (including optional files)
                bool success = await _holdtagservice.UpdateRequest(model, FileUpload1, FileUpload2);

                if (success)
                {
                    TempData["SuccessMessage"] = "Record updated successfully!";
                }
                else
                {
                    TempData["ErrorMessage"] = "No rows were updated. Please check the ID.";
                }

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");

                // Reload data for the view
                var allRecords = await _holdtagservice.GetAllHoldRecordsAsync();
                ViewBag.IssuedByOperators = await _holdtagservice.GetIssuedByOperatorsAsync();
                ViewBag.DispositionOperators = await _holdtagservice.GetDispositionOperatorsAsync();
                ViewBag.ReworkOperators = await _holdtagservice.GetReworkOperatorsAsync();

                TempData["ErrorMessage"] = $"An error occurred while updating: {ex.Message}";
                return View("Index", allRecords);
            }
        }
    }
}
