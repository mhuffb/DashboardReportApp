using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    using DashboardReportApp.Models;
    using DashboardReportApp.Services;
    using Microsoft.AspNetCore.Mvc;
    using System.Threading.Tasks;
    [Route("MaintenanceRequest")]
    public class MaintenanceRequestController : Controller
    {
        private readonly MaintenanceRequestService _service;
        private readonly EmailAttachmentService _emailAttachmentService;

        public MaintenanceRequestController(EmailAttachmentService emailAttachmentService, MaintenanceRequestService service)
        {
            //_emailAttachmentService = emailAttachmentService;
            _service = service;
        }


        [HttpPost("SaveImagePath")]
        public async Task<IActionResult> SaveImagePath(int id, string imagePath)
        {
            bool success = await _service.UpdateMediaLinkFile(id, imagePath);
            if (success)
                TempData["Success"] = "Image path updated successfully.";
            else
                TempData["Error"] = "Failed to update the image path. Please try again.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            var requests = await _service.GetOpenRequestsAsync();

            // Populate Requesters and EquipmentList
            ViewData["Requesters"] = await _service.GetRequestersAsync();
            ViewData["EquipmentList"] = await _service.GetEquipmentListAsync();
          //  await _emailAttachmentService.ProcessIncomingEmailsAsync();
            return View(requests);
        }

        [HttpPost("AddRequest")]
        public async Task<IActionResult> AddRequest(MaintenanceRequestModel request, IFormFile? file)
        {
            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState)
                {
                    Console.WriteLine($"Key: {error.Key}");
                    foreach (var subError in error.Value.Errors)
                    {
                        Console.WriteLine($"Error: {subError.ErrorMessage}");
                    }
                }

                TempData["Error"] = "Invalid input. Please correct the errors.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Handle File Upload
                if (file != null && file.Length > 0)
                {
                    var uploadsFolder = @"\\SINTERGYDC2024\Vol1\VSP\Uploads";
                    var fileExtension = Path.GetExtension(file.FileName);
                    var fileName = $"MaintenanceRequestMedia_{request.Id}{fileExtension}";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    // Ensure the upload directory exists
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    // Save the file
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(fileStream);
                    }

                    // Assign the file path to the request model
                    request.FileAddressMediaLink = filePath;
                    Console.WriteLine($"[DEBUG] File uploaded: {filePath}");
                }
                else
                {
                    request.FileAddressMediaLink = null;
                }

                if (request.DownStatus == true)
                {
                    request.DownStartDateTime = DateTime.Now;
                }

                request.Status = "Open";

                // Call the service to add the request
                bool success = await _service.AddRequestAsync(request);
                TempData["Success"] = success ? "Request added successfully." : "Failed to add the request.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception in AddRequest: {ex.Message}");
                TempData["Error"] = "An error occurred while adding the request.";
            }

            return RedirectToAction(nameof(Index));
        }



        [HttpGet("GenerateQRCode/{id}")]
        public IActionResult GenerateQRCode(int id)
        {
            var mailToLink = $"mailto:fixit@sintergy.net?subject=Maintenance Order #{id}";
            var qrCodeService = new QRCodeService();
            var qrCodeImage = qrCodeService.GenerateQRCode(mailToLink);

            return File(qrCodeImage, "image/png");
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
            // Copy the file to the destination path
            if (!System.IO.File.Exists(destinationPath))
            {
                System.IO.File.Copy(filePath, destinationPath, overwrite: true);
            }

            // Return the relative path to the image
            var relativePath = $"/Uploads/{fileName}";
            return Json(new { success = true, url = relativePath });
        }

       


        [HttpPost]
        public IActionResult UpdateRequest(MaintenanceRequestModel model, IFormFile? FileUpload)
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

        [HttpPost("UpdateMediaLinkFile")]
        public async Task<IActionResult> UpdateMediaLinkFile(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a valid file.";
                Console.WriteLine("[ERROR] No file selected or file is empty.");
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var uploadsFolder = @"\\SINTERGYDC2024\Vol1\VSP\Uploads";
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var fileExtension = Path.GetExtension(file.FileName);
                var fileName = $"ProcessChangeRequestMedia_{id}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                Console.WriteLine($"[DEBUG] Saving file at: {filePath}");

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                Console.WriteLine("[DEBUG] File saved successfully.");

                // Ensure NULL safety when updating the database
                bool success = await _service.UpdateMediaLinkFile(id, filePath ?? "");

                if (success)
                {
                    TempData["Success"] = "File uploaded and link updated successfully.";
                }
                else
                {
                    TempData["Error"] = "Failed to update the image link.";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] File upload failed: {ex.Message}");
                TempData["Error"] = "An error occurred while uploading the file.";
            }

            return RedirectToAction(nameof(Index));
        }


    }

}