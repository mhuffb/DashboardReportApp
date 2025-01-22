using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    using DashboardReportApp.Models;
    using DashboardReportApp.Services;
    using Microsoft.AspNetCore.Mvc;
    using System.Threading.Tasks;

    public class MaintenanceRequestController : Controller
    {
        private readonly MaintenanceRequestService _service;
        private readonly EmailAttachmentService _emailAttachmentService;

        public MaintenanceRequestController(EmailAttachmentService emailAttachmentService, MaintenanceRequestService service)
        {
            _emailAttachmentService = emailAttachmentService;
            _service = service;
        }

        public async Task<IActionResult> FetchEmailAttachments()
        {
            await _emailAttachmentService.ProcessIncomingEmailsAsync();
            TempData["Success"] = "Email attachments processed successfully.";
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> SaveImagePath(int id, string imagePath)
        {
            bool success = await _service.UpdateImagePathAsync(id, imagePath);
            if (success)
                TempData["Success"] = "Image path updated successfully.";
            else
                TempData["Error"] = "Failed to update the image path. Please try again.";

            return RedirectToAction(nameof(Index));
        }
        public async Task<IActionResult> Index()
        {
            var requests = await _service.GetOpenRequestsAsync();

            // Populate Requesters and EquipmentList
            ViewData["Requesters"] = await _service.GetRequestersAsync();
            ViewData["EquipmentList"] = await _service.GetEquipmentListAsync();

            return View(requests);
        }


        [HttpPost]
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
        [Route("MaintenanceRequest/GenerateQRCode/{id}")]
        public IActionResult GenerateQRCode(int id)
        {
            var mailToLink = $"mailto:fixit@sintergy.net?subject=Maintenance Order #{id}";
            var qrCodeService = new QRCodeService();
            var qrCodeImage = qrCodeService.GenerateQRCode(mailToLink);

            return File(qrCodeImage, "image/png");
        }

    }

}
