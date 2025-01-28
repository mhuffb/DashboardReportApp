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
        public IActionResult UpdateRequest(ProcessChangeRequest model, IFormFile FileAddress)
        {
            // Remove FileAddress from validation
            ModelState.Remove("FileAddress");

            if (!ModelState.IsValid)
            {
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
                if (FileAddress != null && FileAddress.Length > 0)
                {
                    // Ensure the uploads folder exists
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    // Create the file name and path
                    var fileName = $"ProcessChangeRequest_{model.Id}{Path.GetExtension(FileAddress.FileName)}";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    // Save the file to the server
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        FileAddress.CopyTo(stream);
                    }

                    // Save the relative path to the database
                    model.FileAddress = $"/uploads/{fileName}"; // Save relative path for web access
                    Console.WriteLine($"File uploaded and saved to: {model.FileAddress}");
                }
                else
                {
                    Console.WriteLine("No file was uploaded.");
                }


                _service.UpdateRequest(model);
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
                return RedirectToAction(nameof(Index));
            }

            // Define the folder to save the uploaded files
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            // Create the file name: ProcessChangeRequest<ID>.<extension>
            var fileExtension = Path.GetExtension(file.FileName);
            var fileName = $"ProcessChangeRequest{id}{fileExtension}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            // Save the file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                file.CopyTo(stream);
            }

            // Save the file path to the database
            var request = _service.GetAllRequests().FirstOrDefault(r => r.Id == id);
            if (request != null)
            {
                request.FileAddressMediaLink = "/uploads/" + fileName; // Save relative path for web access
                _service.UpdateRequest(request);
            }

            TempData["Success"] = "File uploaded and link updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        
    }
}
