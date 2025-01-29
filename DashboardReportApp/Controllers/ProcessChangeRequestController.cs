using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace DashboardReportApp.Controllers
{
    public class ProcessChangeRequestController : Controller
    {
        private readonly ProcessChangeRequestService _service;

        public ProcessChangeRequestController(ProcessChangeRequestService service)
        {
            _service = service;
        }

        public IActionResult Index()
        {
            var requests = _service.GetAllRequests();
            return View(requests);
        }

        public IActionResult AdminView()
        {
            var requests = _service.GetAllRequests();
            return View(requests);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddRequest(ProcessChangeRequest model, IFormFile? file) // Allow null files
        {
            if (!ModelState.IsValid)
            {
                foreach (var state in ModelState)
                {
                    foreach (var error in state.Value.Errors)
                    {
                        Console.WriteLine($"Key: {state.Key}, Error: {error.ErrorMessage}");
                    }
                }

                ViewData["Error"] = "Please fix the validation errors.";
                return View("Index", _service.GetAllRequests());
            }

            try
            {
                _service.AddRequest(model, file);
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ViewData["Error"] = $"An error occurred: {ex.Message}";
                return View("Index", _service.GetAllRequests());
            }
        }

        [HttpPost]
        public IActionResult UpdateRequest(ProcessChangeRequest model, IFormFile? FileUpload)
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

                return View("AdminView", _service.GetAllRequests());
            }

            try
            {
                // Call the service to update the request
                _service.UpdateRequest(model, FileUpload);

                return RedirectToAction("AdminView");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return View("AdminView", _service.GetAllRequests());
            }
        }

        [HttpPost]
        public IActionResult UpdateMediaLinkFile(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a valid file.";
                Console.WriteLine("Error: No file selected or file is empty.");
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Ensure the uploads folder exists
                var uploadsFolder = Path.Combine("wwwroot", "uploads");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                    Console.WriteLine($"Created uploads folder at: {uploadsFolder}");
                }

                // Construct the file name and paths
                var fileExtension = Path.GetExtension(file.FileName);
                var fileName = $"ProcessChangeRequestMedia_{id}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName); // Physical path
                var relativePath = $"/uploads/{fileName}"; // Relative path for database

                Console.WriteLine($"File details: Name = {fileName}, Extension = {fileExtension}");
                Console.WriteLine($"Physical path: {filePath}");
                Console.WriteLine($"Relative path to save: {relativePath}");

                // Save the file to the physical path
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }
                Console.WriteLine($"File successfully saved at: {filePath}");

                // Update the database with the relative path
                var request = _service.GetAllRequests().FirstOrDefault(r => r.Id == id);
                if (request != null)
                {
                    request.FileAddressMediaLink = relativePath; // Use relative path
                    _service.UpdateMediaLinkFile(id, relativePath);
                    Console.WriteLine($"Database updated for Request ID = {id}, FileAddressMediaLink = {relativePath}");
                }
                else
                {
                    Console.WriteLine($"Request with ID = {id} not found.");
                }

                TempData["Success"] = "File uploaded and link updated successfully.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                TempData["Error"] = $"An error occurred: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }


    }
}
