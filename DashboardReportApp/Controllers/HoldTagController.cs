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
            Console.WriteLine("[HoldTag.Index] Start");

            List<string>? parts = null;
            List<string>? operators = null;
            List<HoldTagModel>? records = null;

            try
            {
                Console.WriteLine("[HoldTag.Index] Before GetActiveProlinkParts");
                parts = await _sharedService.GetActiveProlinkParts();
                Console.WriteLine("[HoldTag.Index] After GetActiveProlinkParts");

                Console.WriteLine("[HoldTag.Index] Before GetAllOperators");
                operators = await _sharedService.GetAllOperators();
                Console.WriteLine("[HoldTag.Index] After GetAllOperators");

                Console.WriteLine("[HoldTag.Index] Before GetAllHoldRecordsAsync");
                records = await _holdTagService.GetAllHoldRecordsAsync();
                Console.WriteLine("[HoldTag.Index] After GetAllHoldRecordsAsync");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HoldTag.Index] ERROR: " + ex);
                // Show the error so the page at least loads
                return Content("Error in HoldTag/Index:\n\n" + ex);
            }

            ViewData["Parts"] = parts ?? new List<string>();
            ViewData["Operators"] = operators ?? new List<string>();

            bool isAdmin = User.IsInRole("HoldAdmin");
            ViewBag.IsAdmin = isAdmin;

            if (isAdmin)
            {
                Console.WriteLine("[HoldTag.Index] Before admin operator lists");
                ViewBag.IssuedByOperators = await _holdTagService.GetIssuedByOperatorsAsync();
                ViewBag.DispositionOperators = await _holdTagService.GetDispositionOperatorsAsync();
                ViewBag.ReworkOperators = await _holdTagService.GetReworkOperatorsAsync();
                Console.WriteLine("[HoldTag.Index] After admin operator lists");
            }

            var model = new HoldTagIndexViewModel
            {
                FormModel = new HoldTagModel(),
                Records = records ?? new List<HoldTagModel>()
            };

            Console.WriteLine("[HoldTag.Index] Returning view");
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
                    var originalName = Path.GetFileName(file.FileName);
                    var storedName = $"HoldTagFile1_{record.Id}_{DateTime.UtcNow.Ticks}{Path.GetExtension(originalName)}";

                    var fullPath = _holdTagService.GetUploadFullPath(storedName);

                    Console.WriteLine($"[HoldTag] Uploading to: {fullPath}");

                    using (var stream = System.IO.File.Create(fullPath))
                    {
                        await file.CopyToAsync(stream);
                    }

                    record.FileAddress1 = storedName;
                }
                var pdfPath = _holdTagService.GenerateHoldTagPdf(record);

                // Resolve optional attachment (works for filename or legacy full path)
                var attachment1Abs = _holdTagService.GetAbsolutePath(record.FileAddress1);

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

        // HoldTagController.cs
        [HttpPost("UpdateFile")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateFile(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return RedirectToAction("Index"); // or BadRequest if you prefer JSON

            try
            {
                // Save using fixed name, just like Maintenance does (filename-only in DB)
                var ext = Path.GetExtension(file.FileName);
                var fileName = $"HoldTagFile1_{id}{ext}";
                var fullPath = _holdTagService.GetUploadFullPath(fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

                using (var stream = System.IO.File.Create(fullPath))
                    await file.CopyToAsync(stream);

                await _holdTagService.UpdateFileAddress1Async(id, fileName);

                TempData["SuccessMessage"] = "File uploaded and updated successfully.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error updating file: {ex.Message}";
                return RedirectToAction("Index");
            }
        }


        // IMPORTANT: give it the explicit template so /HoldTag/FetchImage works
        [HttpGet("FetchImage")]
        public IActionResult FetchImage(string filePath)
        {
            var fileName = Path.GetFileName(filePath ?? "");
            if (string.IsNullOrWhiteSpace(fileName))
                return Json(new { success = false, message = "No file provided." });

            // You already have StreamFile; reuse it (like Maintenance's Preview)
            var url = Url.Action(nameof(StreamFile), "HoldTag", new { name = fileName }, Request.Scheme);
            return Json(new { success = true, url });
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

                var abs = _holdTagService.GetExistingFilePath(name);
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
