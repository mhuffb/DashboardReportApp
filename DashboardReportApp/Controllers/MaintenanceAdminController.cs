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
        public async Task<IActionResult> AdminView()
        {
            Console.WriteLine("AdminView was called");

            var requests = _adminService.GetAllRequests();
            var operatorNames = _adminService.GetAllOperatorNames();
            // NEW: Get the equipment list from the maintenance table
            var equipmentList = _adminService.GetEquipmentListAsync();

            if (requests == null)
            {
                Console.WriteLine("Requests are null!");
            }
            ViewBag.OperatorNames = operatorNames;
            ViewData["EquipmentList"] = await _adminService.GetEquipmentListAsync();
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
        public async Task<IActionResult> UpdateRequest(
           MaintenanceRequestModel model,
           IFormFile? FileAddress1,
           IFormFile? FileAddress2
       )
        {
            try
            {
                // We will store both files in the same folder:
                var uploadsFolder = @"\\SINTERGYDC2024\Vol1\VSP\Uploads";
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // == FILE 1 ==
                if (FileAddress1 != null && FileAddress1.Length > 0)
                {
                    var fileExtension = Path.GetExtension(FileAddress1.FileName);
                    var fileName = $"MaintenanceRequestFile1_{model.Id}{fileExtension}";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    // If a file with that name already exists, delete it
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }

                    // Save the uploaded file to the server
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await FileAddress1.CopyToAsync(stream);
                    }

                    // Store in model so it can be updated in DB
                    model.FileAddress1 = filePath;
                }

                // == FILE 2 ==
                if (FileAddress2 != null && FileAddress2.Length > 0)
                {
                    var fileExtension2 = Path.GetExtension(FileAddress2.FileName);
                    var fileName2 = $"MaintenanceRequestFile2_{model.Id}{fileExtension2}";
                    var filePath2 = Path.Combine(uploadsFolder, fileName2);

                    if (System.IO.File.Exists(filePath2))
                    {
                        System.IO.File.Delete(filePath2);
                    }

                    using (var stream = new FileStream(filePath2, FileMode.Create))
                    {
                        await FileAddress2.CopyToAsync(stream);
                    }

                    model.FileAddress2 = filePath2;
                }

                // Merge new status text with old
                string oldDesc = model.StatusDesc ?? "";
                string nowString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string newLine = $"{model.Status} {nowString} {model.StatusUpdatedBy} {model.NewStatusDesc}".Trim();

                model.StatusDesc = string.IsNullOrEmpty(oldDesc)
                    ? newLine
                    : (oldDesc + "\n" + newLine);

                // If status is "Closed," set ClosedDateTime
                if (model.Status == "Closed")
                {
                    model.ClosedDateTime = DateTime.Now;
                }

                // Update in DB (adjust your method signature if needed)
                _adminService.UpdateRequest(model);

                TempData["Success"] = "Request updated successfully.";
                return RedirectToAction("AdminView");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                TempData["Error"] = "Failed to update the request.";

                // Re-fetch requests...
                var requests = _adminService.GetAllRequests();
                ViewBag.OperatorNames = _adminService.GetAllOperatorNames();

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
