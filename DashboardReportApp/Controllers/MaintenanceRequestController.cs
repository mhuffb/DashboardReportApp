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

            return View(requests);
        }

        [HttpPost("AddRequest")]
        public async Task<IActionResult> AddRequest(MaintenanceRequest request)
        {
            if (ModelState.IsValid)
            {
                bool success = await _service.AddRequestAsync(request);
                if (success)
                    TempData["Success"] = "Request added successfully.";
                else
                    TempData["Error"] = "Failed to add the request. Please try again.";
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
            try
            {
                var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
                if (!Directory.Exists(uploadsPath))
                {
                    Directory.CreateDirectory(uploadsPath);
                }

                string fileName = Path.GetFileName(filePath);
                string localFilePath = Path.Combine(uploadsPath, fileName);

                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Copy(filePath, localFilePath, true);
                    string relativeUrl = $"/uploads/{fileName}";
                    return Json(new { success = true, url = relativeUrl });
                }
                else
                {
                    return Json(new { success = false, message = "File not found on the network location." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

}