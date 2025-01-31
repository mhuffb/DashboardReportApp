using Microsoft.AspNetCore.Mvc;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using System.Threading.Tasks;
using DashboardReportApp.Controllers.Attributes;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")] // Set your password here
    [Route("MaintenanceAdmin")]
    public class MaintenanceAdminController : Controller
    {
        private readonly MaintenanceAdminService _adminService;

        public MaintenanceAdminController(MaintenanceAdminService adminService)
        {
            _adminService = adminService;
        }

        [HttpGet("AdminView")]
        public IActionResult AdminView()
        {
            Console.WriteLine("AdminView was called");

            var requests = _adminService.GetAllRequests();
            if (requests == null)
            {
                Console.WriteLine("Requests are null!");
            }

            return View(requests);
        }


        [HttpPost]
        public async Task<IActionResult> UpdateRequest(MaintenanceRequestModel model, IFormFile? FileUpload)
        {
            if (!ModelState.IsValid)
            {
                return View("AdminView", _adminService.GetAllRequests());
            }

            try
            {
                if (FileUpload != null && FileUpload.Length > 0)
                {
                    var uploadsFolder = @"\\SINTERGYDC2024\Vol1\VSP\Uploads";
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var fileExtension = Path.GetExtension(FileUpload.FileName);
                    var fileName = $"MaintenanceRequest_{model.Id}{fileExtension}";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await FileUpload.CopyToAsync(stream);
                    }

                    model.FileAddress = filePath; // Save new file path in database
                }

                _adminService.UpdateRequest(model, FileUpload);

                TempData["Success"] = "Request updated successfully.";
                return RedirectToAction("AdminView");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                TempData["Error"] = "Failed to update the request.";
                return View("AdminView", _adminService.GetAllRequests());
            }
        }
        [HttpGet("FetchImage")]
        public IActionResult FetchImage(string filePath)
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
            var fileName = Path.GetFileName(filePath);
            var destinationDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads");
            var destinationPath = Path.Combine(destinationDir, fileName);

            if (!Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir); // Create the directory if it doesn't exist
            }

            // Copy the file to the destination path if it doesn't already exist
            if (!System.IO.File.Exists(destinationPath))
            {
                System.IO.File.Copy(filePath, destinationPath);
            }

            // Return the relative path to the image
            var relativePath = $"/Uploads/{fileName}";
            return Json(new { success = true, url = relativePath });
        }
    }
}
