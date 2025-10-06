using System.Threading.Tasks;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    [Route("HoldTag")]
    public class HoldTagController : Controller
    {
        private readonly HoldTagService _holdTagService;
        private readonly SharedService _sharedService;


        public HoldTagController(HoldTagService service, SharedService sharedService)
        {
            _holdTagService = service;
            _sharedService = sharedService;
        }

        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            var parts = await _sharedService.GetActiveProlinkParts();
            var operators = await _sharedService.GetAllOperators();
            var records = await _holdTagService.GetAllHoldRecordsAsync();

            ViewData["Parts"] = parts ?? new List<string>();
            ViewData["Operators"] = operators;

            var model = new HoldTagIndexViewModel
            {
                FormModel = new HoldTagModel(), // Empty model for the form
                Records = records // List of records for the table
            };

            return View(model);
        }





        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(HoldTagModel record, IFormFile? file)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please correct the errors and try again.";
                ViewData["Operators"] = await _sharedService.GetAllOperators();
                return View("Index", record);
            }

            try
            {
                record.Date = DateTime.Now;

                int newId = await _holdTagService.AddHoldRecordAsync(record);
                record.Id = newId;

                if (file != null && file.Length > 0)
                {
                    var savedPath = _holdTagService.SaveHoldFile(file, record.Id, "HoldTagFile1");
                    record.FileAddress1 = savedPath;
                    await _holdTagService.UpdateFileAddress1Async(record.Id, savedPath);
                }

                var pdfPath = _holdTagService.GenerateHoldTagPdf(record);
                _sharedService.PrintFileToSpecificPrinter("QaholdTags", pdfPath, record.Quantity.GetValueOrDefault(1));

                string subject = $"{record.Part} Placed on Hold By: {record.IssuedBy}";
                string body = $"Discrepancy: {record.Discrepancy}\nQuantity: {record.Quantity} {record.Unit}\nIssued By: {record.IssuedBy}\nIssued Date: {record.Date:MM/dd/yyyy}";
                _sharedService.SendEmailWithAttachment("holdtag@sintergy.net", pdfPath, record.FileAddress1, subject, body);

                TempData["SuccessMessage"] = "Hold record submitted and email sent successfully!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpPost("UpdateFile")]
        public async Task<IActionResult> UpdateFile(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "Please select a valid file.";
                return RedirectToAction("Index");
            }

            try
            {
                var savedPath = _holdTagService.SaveHoldFile(file, id, "HoldTagFile1");
                await _holdTagService.UpdateFileAddress1Async(id, savedPath);

                TempData["SuccessMessage"] = "File uploaded and updated successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error updating file: {ex.Message}";
            }

            return RedirectToAction("Index");
        }


        [HttpGet("FetchImage")]
        public IActionResult FetchImage(string filePath)
        {
            try
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
                Console.WriteLine($"[DEBUG] Copying from '{filePath}' to '{destinationPath}'...");
                // Copy the file to the destination path
                if (!System.IO.File.Exists(destinationPath))
                {
                    System.IO.File.Copy(filePath, destinationPath, overwrite: true);
                }

                // Return the relative path to the image
                var relativePath = $"/Uploads/{fileName}";
                return Json(new { success = true, url = relativePath });
            }
            catch (Exception ex)
            {
                // Log exception so you see EXACT error cause
                Console.WriteLine($"[ERROR] FetchImage exception: {ex}");
                // Return an appropriate error response
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
       

    }

}
