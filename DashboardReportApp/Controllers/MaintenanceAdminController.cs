using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")] // Set your password here
    [Route("MaintenanceAdmin")]
    public class MaintenanceAdminController : Controller
    {
        private readonly MaintenanceAdminService _adminService;
        private readonly PathOptions _paths;
        public MaintenanceAdminController(
        MaintenanceAdminService adminService,
        IOptions<PathOptions> pathOptions)
        {
            _adminService = adminService;
            _paths = pathOptions.Value;
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
          IFormFile? FileAddress2)
        {
            try
            {
                Directory.CreateDirectory(_paths.MaintenanceUploads);

                // FILE 1
                if (FileAddress1 is { Length: > 0 })
                {
                    var ext = Path.GetExtension(FileAddress1.FileName);
                    var fileName = $"MaintenanceRequestFile1_{model.Id}{ext}";
                    var full = Path.Combine(_paths.MaintenanceUploads, fileName);

                    if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
                    using var s = new FileStream(full, FileMode.Create);
                    await FileAddress1.CopyToAsync(s);

                    model.FileAddress1 = fileName; // filename only
                }

                // FILE 2
                if (FileAddress2 is { Length: > 0 })
                {
                    var ext2 = Path.GetExtension(FileAddress2.FileName);
                    var fileName2 = $"MaintenanceRequestFile2_{model.Id}{ext2}";
                    var full2 = Path.Combine(_paths.MaintenanceUploads, fileName2);

                    if (System.IO.File.Exists(full2)) System.IO.File.Delete(full2);
                    using var s2 = new FileStream(full2, FileMode.Create);
                    await FileAddress2.CopyToAsync(s2);

                    model.FileAddress2 = fileName2; // filename only
                }

                // Append new status line
                string oldDesc = model.StatusDesc ?? "";
                string nowString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string newLine = $"{model.Status} {nowString} {model.StatusUpdatedBy} {model.NewStatusDesc}".Trim();
                model.StatusDesc = string.IsNullOrEmpty(oldDesc) ? newLine : (oldDesc + "\n" + newLine);

                if (model.Status == "Closed")
                    model.ClosedDateTime = DateTime.Now;

                _adminService.UpdateRequest(model);

                TempData["Success"] = "Request updated successfully.";
                return RedirectToAction("AdminView");
            }
            catch
            {
                TempData["Error"] = "Failed to update the request.";
                var requests = _adminService.GetAllRequests();
                return View("AdminView", requests);
            }
        }

        [HttpGet("FetchImage")]
        public IActionResult FetchImage(string filePath)
        {
            var fileName = Path.GetFileName(filePath ?? "");
            if (string.IsNullOrWhiteSpace(fileName))
                return Json(new { success = false, message = "No file provided." });

            var url = Url.Action("Preview", "MaintenanceAdmin", new { file = fileName }, Request.Scheme);
            return Json(new { success = true, url });
        }

        [HttpGet("Preview")]
        public IActionResult Preview(string file)
        {
            var fileName = Path.GetFileName(file ?? "");
            if (string.IsNullOrWhiteSpace(fileName))
                return NotFound();

            var full = Path.Combine(_paths.MaintenanceUploads, fileName);
            if (!System.IO.File.Exists(full))
                return NotFound();

            var ext = Path.GetExtension(full).ToLowerInvariant();
            var mime = ext switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };

            var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, mime);
        }
    }
}
