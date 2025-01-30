using Microsoft.AspNetCore.Mvc;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
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
            var requests = _adminService.GetAllRequests();
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


    }
}
