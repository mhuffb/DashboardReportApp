using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.IO;
using System.Text.Json; // at top

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")] // Set your password here
    [Route("AdminDeviation")]
    public class AdminDeviationController : Controller
    {
        private readonly AdminDeviationService _deviationService;

        public AdminDeviationController(AdminDeviationService deviationService)
        {
            _deviationService = deviationService;
        }

        [HttpGet("Index")]
        public IActionResult Index()
        {
            List<AdminDeviationModel> deviations = _deviationService.GetAllDeviations();
            ViewBag.OperatorNames = _deviationService.GetAllOperatorNames();
            ViewBag.ApprovedByOperators = _deviationService.GetApprovedByOperators();
            return View(deviations);
        }

        [HttpPost("Update")]
        [ValidateAntiForgeryToken]
        public IActionResult Update(
       [Bind("Id,Part,SentDateTime,Discrepancy,Operator,CommMethod,Disposition,ApprovedBy,DateTimeCASTReview,FileAddress1,FileAddress2")]
    AdminDeviationModel model,
       IFormFile? file1,
       IFormFile? file2)
        {
            // Remove server-only fields that aren’t posted
            ModelState.Remove(nameof(AdminDeviationModel.Timestamp)); // server-managed
                                                                      // (Add more removes for any other not-posted, non-nullable props if needed)

            if (!ModelState.IsValid)
            {
                var lines = ModelState
                    .Where(kv => kv.Value?.Errors?.Count > 0)
                    .SelectMany(kv => kv.Value!.Errors.Select(e =>
                        $"{kv.Key}: {e.ErrorMessage}  (attempted: '{kv.Value!.AttemptedValue}')"))
                    .ToList();

                if (lines.Count == 0) lines.Add("ModelState invalid but no individual field errors were found.");

                TempData["SaveFailed"] = "Validation failed.";
                TempData["ModelErrorsText"] = string.Join("\n", lines);

                // also write to server console/log for good measure
                System.Console.Error.WriteLine("AdminDeviation Update validation errors:\n" + TempData["ModelErrorsText"]);

                return RedirectToAction("Index");
            }

            _deviationService.UpdateDeviation(model, file1, file2);
            TempData["Success"] = "Deviation updated successfully!";
            return RedirectToAction("Index");
        }

        [HttpGet("FetchFile")]
        public IActionResult FetchFile(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Json(new { success = false, message = "No file name provided." });

            // Build absolute path from stored filename
            var abs = _deviationService.GetAbsolutePath(name);
            if (string.IsNullOrEmpty(abs) || !System.IO.File.Exists(abs))
                return Json(new { success = false, message = $"File not found: {name}" });

            // Give the client a URL that streams inline
            var url = Url.Action(nameof(StreamFile), "AdminDeviation", new { name }, Request.Scheme);
            return Json(new { success = true, url });
        }

        [HttpGet("StreamFile")]
        public IActionResult StreamFile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return NotFound();

            var abs = _deviationService.GetAbsolutePath(name);
            if (string.IsNullOrEmpty(abs) || !System.IO.File.Exists(abs)) return NotFound();

            var ext = Path.GetExtension(abs).ToLowerInvariant();
            var mime = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };

            // Inline preview; enables range requests for PDFs/images
            return PhysicalFile(abs, mime, enableRangeProcessing: true);
        }
    }
}
