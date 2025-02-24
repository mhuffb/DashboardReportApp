using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Linq;

namespace DashboardReportApp.Controllers
{
    [Route("ProcessChangeRequest")]
    public class ProcessChangeRequestController : Controller
    {
        private readonly ProcessChangeRequestService _service;

        public ProcessChangeRequestController(ProcessChangeRequestService service)
        {
            _service = service;
        }

        // Display table of existing requests (read-only) + an "Add" row
        [Route("Index")]
        public IActionResult Index()
        {
            var requests = _service.GetAllRequests();

            // Load operator names from the DB
            // e.g. _operatorService.GetAllOperatorNames() => List<string>
            var operators = _service.GetOperators();
            ViewBag.Operators = operators;

            return View(requests);
        }


        // Add a new request
        [Route("AddRequest")]
        [ValidateAntiForgeryToken]
        public IActionResult AddRequest(ProcessChangeRequestModel model, IFormFile? file) // 'file' can be null
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

                // Show same view with error
                ViewData["Error"] = "Please fix the validation errors.";
                return View("Index", _service.GetAllRequests());
            }

            try
            {
                // 1) Create the request row (no file yet).
                int newRequestId = _service.AddRequest(model);

                // 2) If a file was uploaded, call the same logic as "update file"
                if (file != null && file.Length > 0)
                {
                    _service.UpdateFileAddress1(newRequestId, file);
                }

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ViewData["Error"] = $"An error occurred: {ex.Message}";
                return View("Index", _service.GetAllRequests());
            }
        }

        // Upload a file for an existing request, if needed
        [HttpPost("UpdateFileAddress1")]
        public IActionResult UpdateFileAddress1(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a valid file.";
                Console.WriteLine("Error: No file selected or file is empty.");
                return RedirectToAction(nameof(Index));
            }

            try
            {

                _service.UpdateFileAddress1(id, file);

                TempData["Success"] = "File uploaded and link updated successfully.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                TempData["Error"] = $"An error occurred: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
        [HttpGet("FetchImage")]
        public IActionResult FetchImage(string filePath)
        {
            try
            {


                if (string.IsNullOrEmpty(filePath))
                {
                    return Json(new { success = false, message = "No file path provided." });
                }

                if (!System.IO.File.Exists(filePath))
                {
                    return Json(new { success = false, message = $"File not found: {filePath}" });
                }


                // Ensure the directory exists
                var fileName = System.IO.Path.GetFileName(filePath);
                var destinationDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Uploads");
                var destinationPath = Path.Combine(destinationDir, fileName);

                if (!Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir); // Create the directory if it doesn't exist
                }
                if (System.IO.File.Exists(destinationPath))
                {
                    System.IO.File.Delete(destinationPath);
                }
                Console.WriteLine($"[DEBUG] Copying from '{filePath}' to '{destinationPath}'...");
                // Copy the file to the destination path
                if (!System.IO.File.Exists(destinationPath))
                {
                    System.IO.File.Copy(filePath, destinationPath, overwrite: true);
                }

                // Return the relative path to the image
                var relativePath = $"/Uploads/{fileName}";
                return Json(new { success = true, url = relativePath });
            }
            catch (Exception ex)
            {
                // Log exception so you see EXACT error cause
                Console.WriteLine($"[ERROR] FetchImage exception: {ex}");
                // Return an appropriate error response
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
