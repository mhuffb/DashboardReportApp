using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    [Route("HoldTag")]
    public class HoldTagController : Controller
    {
        private readonly HoldTagService _holdTagService;
        private readonly SharedService _sharedService;
        private readonly string _holdTagEmailTo;
        private readonly string _holdTagPrinter;
        public HoldTagController(HoldTagService service,
                         SharedService sharedService,
                         IConfiguration cfg)
        {
            _holdTagService = service;
            _sharedService = sharedService;

            // Configurable email + printer, with sane fallbacks
            _holdTagEmailTo = cfg["Email:HoldTagTo"]
                         ?? cfg["Email:DevTo"]
                         ?? "holdtag@sintergy.net";
            _holdTagPrinter = cfg["Printers:HoldTag"] ?? "QaholdTags";
        }
        // GET: HoldTag/Admin  → password-protected admin view of Hold Tags
        [PasswordProtected(Password = "5intergy")] // same attribute you used on AdminHoldTag
        [HttpGet("Admin")]
        public async Task<IActionResult> Admin()
        {
            // same data as Index, but explicitly in "admin mode"
            var parts = await _sharedService.GetActiveProlinkParts();
            var operators = await _sharedService.GetAllOperators();
            var records = await _holdTagService.GetAllHoldRecordsAsync();

            ViewData["Parts"] = parts ?? new List<string>();
            ViewData["Operators"] = operators;

            ViewBag.IsAdmin = true; // let the view know to show edit controls
            ViewBag.IssuedByOperators = await _holdTagService.GetIssuedByOperatorsAsync();
            ViewBag.DispositionOperators = await _holdTagService.GetDispositionOperatorsAsync();
            ViewBag.ReworkOperators = await _holdTagService.GetReworkOperatorsAsync();

            var model = new HoldTagIndexViewModel
            {
                FormModel = new HoldTagModel(),
                Records = records
            };

            // reuse the same view as the normal HoldTag page
            return View("Index", model);
        }


        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            var parts = await _sharedService.GetActiveProlinkParts();
            var operators = await _sharedService.GetAllOperators();
            var records = await _holdTagService.GetAllHoldRecordsAsync();

            ViewData["Parts"] = parts ?? new List<string>();
            ViewData["Operators"] = operators;

            // TODO: replace with your real admin detection (role, claim, etc.)
            bool isAdmin = User.IsInRole("HoldAdmin");
            ViewBag.IsAdmin = isAdmin;

            if (isAdmin)
            {
                ViewBag.IssuedByOperators = await _holdTagService.GetIssuedByOperatorsAsync();
                ViewBag.DispositionOperators = await _holdTagService.GetDispositionOperatorsAsync();
                ViewBag.ReworkOperators = await _holdTagService.GetReworkOperatorsAsync();
            }

            var model = new HoldTagIndexViewModel
            {
                FormModel = new HoldTagModel(),
                Records = records
            };

            return View(model);
        }

        [HttpGet("ProdNumbersForPart")]
        public async Task<IActionResult> ProdNumbersForPart(string part)
        {
            if (string.IsNullOrWhiteSpace(part))
                return Json(Array.Empty<string>());

            var list = await _holdTagService.GetLastProdNumbersForPartAsync(part);
            return Json(list);
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

                // Save base record (no file yet)
                int newId = await _holdTagService.AddHoldRecordAsync(record);
                record.Id = newId;

                // If there is an upload, save it and store only the filename in DB
                if (file != null && file.Length > 0)
                {
                    var fileNameOnly = _holdTagService.SaveHoldFile(file, record.Id, "HoldTagFile1");
                    record.FileAddress1 = fileNameOnly;
                    await _holdTagService.UpdateFileAddress1Async(record.Id, fileNameOnly);
                }

                var pdfPath = _holdTagService.GenerateHoldTagPdf(record);

                // Resolve optional attachment (works for filename or legacy full path)
                var attachment1Abs = _holdTagService.GetUploadsAbsolutePath(record.FileAddress1);

                _sharedService.PrintFileToSpecificPrinter(_holdTagPrinter, pdfPath, record.Quantity.GetValueOrDefault(1));

                string subject = $"{record.Part} Placed on Hold By: {record.IssuedBy}";
                string body =
                    $"Discrepancy: {record.Discrepancy}\n" +
                    $"Production Number: {record.ProdNumber ?? "N/A"}\n" +
                    $"Quantity: {record.Quantity} {record.Unit}\n" +
                    $"Issued By: {record.IssuedBy}\n" +
                    $"Issued Date: {record.Date:MM/dd/yyyy}";

                // Pass absolute path (or empty) so the emailer can attach it if present
                _sharedService.SendEmailWithAttachment(_holdTagEmailTo, pdfPath, attachment1Abs, subject, body);
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
                // Save and store filename only
                var fileNameOnly = _holdTagService.SaveHoldFile(file, id, "HoldTagFile1");
                await _holdTagService.UpdateFileAddress1Async(id, fileNameOnly);

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
                    return Json(new { success = false, message = "No file path provided." });

                // Resolve filename or legacy full path to an actual existing path
                var abs = _holdTagService.GetExistingFilePath(filePath);

                var fileName = Path.GetFileName(abs);
                var destinationDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads");
                var destinationPath = Path.Combine(destinationDir, fileName);

                if (!Directory.Exists(destinationDir))
                    Directory.CreateDirectory(destinationDir);

                if (System.IO.File.Exists(destinationPath))
                    System.IO.File.Delete(destinationPath);

                System.IO.File.Copy(abs, destinationPath, overwrite: true);

                var relativePath = $"/Uploads/{fileName}";
                return Json(new { success = true, url = relativePath });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] FetchImage exception: {ex}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
        // POST: HoldTag/UpdateRequest
        [HttpPost("UpdateRequest")]
        [ValidateAntiForgeryToken]
        // you can also decorate just this action with [PasswordProtected] if you want:
        [PasswordProtected(Password = "5intergy")] // optional: or use [Authorize(Roles = "HoldAdmin")]
        public async Task<IActionResult> UpdateRequest(HoldTagModel model, IFormFile? FileUpload1, IFormFile? FileUpload2)
        {
            if (!ModelState.IsValid)
            {
                // log for debugging if you like
                foreach (var state in ModelState)
                {
                    foreach (var error in state.Value.Errors)
                    {
                        Console.WriteLine($"Key: {state.Key}, Error: {error.ErrorMessage}");
                    }
                }

                TempData["ErrorMessage"] = "Please correct the errors and try again.";

                // re-hydrate the index view
                var parts = await _sharedService.GetActiveProlinkParts();
                var ops = await _sharedService.GetAllOperators();
                var records = await _holdTagService.GetAllHoldRecordsAsync();

                ViewData["Parts"] = parts ?? new List<string>();
                ViewData["Operators"] = ops;

                ViewBag.IsAdmin = true; // if they could reach this action, they’re admin
                ViewBag.IssuedByOperators = await _holdTagService.GetIssuedByOperatorsAsync();
                ViewBag.DispositionOperators = await _holdTagService.GetDispositionOperatorsAsync();
                ViewBag.ReworkOperators = await _holdTagService.GetReworkOperatorsAsync();

                var vm = new HoldTagIndexViewModel
                {
                    FormModel = new HoldTagModel(),
                    Records = records
                };

                return View("Index", vm);
            }

            try
            {
                var ok = await _holdTagService.UpdateHoldRecordAsync(model, FileUpload1, FileUpload2);
                TempData["SuccessMessage"] = ok
                    ? "Record updated successfully!"
                    : "No rows were updated. Please check the ID.";

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating HoldRecord: {ex.Message}");
                TempData["ErrorMessage"] = $"An error occurred while updating: {ex.Message}";
                return RedirectToAction("Index");
            }
        }
        [HttpGet("FetchFile")]
        public IActionResult FetchFile(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Json(new { success = false, message = "No file name provided." });

            var abs = _holdTagService.GetAbsolutePath(name);
            if (string.IsNullOrEmpty(abs) || !System.IO.File.Exists(abs))
                return Json(new { success = false, message = $"File not found: {name}" });

            var url = Url.Action(nameof(StreamFile), "HoldTag", new { name }, Request.Scheme);
            return Json(new { success = true, url });
        }

        [HttpGet("StreamFile")]
        public IActionResult StreamFile(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return NotFound();

                var abs = _holdTagService.GetAbsolutePath(name);
                if (string.IsNullOrEmpty(abs) || !System.IO.File.Exists(abs))
                    return NotFound();

                var ext = Path.GetExtension(abs).ToLowerInvariant();
                var mime = ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".gif" => "image/gif",
                    ".bmp" => "image/bmp",
                    ".webp" => "image/webp",
                    ".pdf" => "application/pdf",
                    _ => "application/octet-stream"
                };

                Response.Headers.CacheControl = "public,max-age=86400";
                return PhysicalFile(abs, mime, enableRangeProcessing: true);
            }
            catch (OperationCanceledException)
            {
                return new EmptyResult();
            }
        }

    }
}
