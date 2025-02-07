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
            var operatorNames = _adminService.GetAllOperatorNames(); // from step #2
            if (requests == null)
            {
                Console.WriteLine("Requests are null!");
            }
            ViewBag.OperatorNames = operatorNames;
            return View(requests);
        }

        [HttpGet("AllRequestsApi")]
        public IActionResult GetAllRequestsApi()
        {
            var requests = _adminService.GetAllRequests();
            return Ok(requests); // JSON
        }

        [Route("UpdateRequest")]
        [HttpPost]
        public async Task<IActionResult> UpdateRequest(MaintenanceRequestModel model, IFormFile? FileUpload)
        {
         
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

                    // 🔥 Check if the file already exists and delete it
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await FileUpload.CopyToAsync(stream);
                    }

                    model.FileAddress = filePath; // Save new file path in database
                }

                // 1) Grab the old StatusDesc (already in model from form post, or re-fetch from DB if needed)
                string oldDesc = model.StatusDesc ?? "";
                string nowString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // e.g. "Closed 2025-02-05 14:32:55 Bob This is new text"
                string newLine = $"{model.Status} {nowString} {model.StatusUpdatedBy} {model.NewStatusDesc}".Trim();

                // If we want a newline in between old and new
                model.StatusDesc = string.IsNullOrEmpty(oldDesc)
                    ? newLine
                    : (oldDesc + "\n" + newLine);


                // 1) Automatically set ClosedDateTime when status is "Closed"
                if (model.Status == "Closed")
                {
                    model.ClosedDateTime = DateTime.Now;
                }


                // Now update in the DB
                _adminService.UpdateRequest(model, FileUpload);

                TempData["Success"] = "Request updated successfully.";
                return RedirectToAction("AdminView");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                TempData["Error"] = "Failed to update the request.";

                // Re-fetch requests...
                var requests = _adminService.GetAllRequests();

                // MISSING: We must also re-fetch operator names
                ViewBag.OperatorNames = _adminService.GetAllOperatorNames();

                // Then return the same view
                return View("AdminView", requests);
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
             if (System.IO.File.Exists(destinationPath))
                    {
                        System.IO.File.Delete(destinationPath);
                    }
            // Copy the file to the destination path if it doesn't already exist
            if (!System.IO.File.Exists(destinationPath))
            {
                System.IO.File.Copy(filePath, destinationPath, overwrite: true);
            }

            // Return the relative path to the image
            var relativePath = $"/Uploads/{fileName}";
            return Json(new { success = true, url = relativePath });
        }
    }
}
