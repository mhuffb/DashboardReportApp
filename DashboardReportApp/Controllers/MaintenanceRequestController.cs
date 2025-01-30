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
            bool success = await _service.UpdateMediaLinkFile(id, imagePath);
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
            var destinationDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Uploads");
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
                var uploadsFolder = @"\\SINTERGYDC2024\Vol1\Visual Studio Programs\VSP\Uploads";
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                    Console.WriteLine($"Created uploads folder at: {uploadsFolder}");
                }

                // Construct the file name and paths
                var fileExtension = Path.GetExtension(file.FileName);
                var fileName = $"ProcessChangeRequestMedia_{id}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName); // Physical path

                Console.WriteLine($"File details: Name = {fileName}, Extension = {fileExtension}");
                Console.WriteLine($"Physical path: {filePath}");

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
                    request.FileAddressMediaLink = filePath; // Use relative path
                    _service.UpdateMediaLinkFile(id, filePath);
                    Console.WriteLine($"Database updated for Request ID = {id}, FileAddressMediaLink = {filePath}");
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