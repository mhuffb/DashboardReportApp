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
            _emailAttachmentService = emailAttachmentService;
            _service = service;
        }

        [HttpGet("FetchEmailAttachments")]
        public async Task<IActionResult> FetchEmailAttachments()
        {
            await _emailAttachmentService.ProcessIncomingEmailsAsync();
            TempData["Success"] = "Email attachments processed successfully.";
            return RedirectToAction("Index");
        }

        [HttpPost("SaveImagePath")]
        public async Task<IActionResult> SaveImagePath(int id, string imagePath)
        {
            bool success = await _service.UpdateImagePathAsync(id, imagePath);
            if (success)
                TempData["Success"] = "Image path updated successfully.";
            else
                TempData["Error"] = "Failed to update the image path. Please try again.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var requests = await _service.GetOpenRequestsAsync();

            // Populate Requesters and EquipmentList
            ViewData["Requesters"] = await _service.GetRequestersAsync();
            ViewData["EquipmentList"] = await _service.GetEquipmentListAsync();
            await _emailAttachmentService.ProcessIncomingEmailsAsync();
            return View(requests);
        }

        [HttpPost("AddRequest")]
        public async Task<IActionResult> AddRequest(MaintenanceRequestModel request)
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

            bool success = await _service.AddRequestAsync(request);
            TempData["Success"] = success ? "Request added successfully." : "Failed to add the request.";

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
            var destinationDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images");
            var destinationPath = Path.Combine(destinationDir, fileName);

            if (!Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir); // Create the directory if it doesn't exist
            }

            // Copy the file to the destination path
            if (!System.IO.File.Exists(destinationPath))
            {
                System.IO.File.Copy(filePath, destinationPath);
            }

            // Return the relative path to the image
            var relativePath = $"/images/{fileName}";
            return Json(new { success = true, url = relativePath });
        }

    }

}