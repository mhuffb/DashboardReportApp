using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using static DashboardReportApp.Services.HoldTagService;

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
            
            var operators = await _sharedService.GetAllOperators();
            var records = await _holdTagService.GetAllHoldRecordsAsync();

            
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

                string? tempName = null;
                string? finalName = null;

                // 1) If a file is uploaded, save it with a temporary unique name
                if (file is { Length: > 0 })
                {
                    var ext = Path.GetExtension(file.FileName);
                    tempName = $"HoldTagFile1_{Guid.NewGuid()}{ext}";
                    var tempFullPath = _holdTagService.GetUploadFullPath(tempName);

                    using (var stream = System.IO.File.Create(tempFullPath))
                        await file.CopyToAsync(stream);

                    // Store the TEMP filename so the INSERT includes *a* filename
                    record.FileAddress1 = tempName;
                }

                // 2) Insert (includes FileAddress1 if present)
                int newId = await _holdTagService.AddHoldRecordAsync(record);
                record.Id = newId;

                // 3) If we had a temp file, rename it to the final pattern and persist the final name
                if (!string.IsNullOrWhiteSpace(tempName))
                {
                    var ext = Path.GetExtension(tempName);
                    finalName = $"HoldTagFile1_{record.Id}{ext}";

                    var tempFullPath = _holdTagService.GetUploadFullPath(tempName);
                    var finalFullPath = _holdTagService.GetUploadFullPath(finalName);

                    if (System.IO.File.Exists(finalFullPath))
                        System.IO.File.Delete(finalFullPath);

                    System.IO.File.Move(tempFullPath, finalFullPath);

                    // Update DB to final filename
                    await _holdTagService.UpdateFileAddress1Async(record.Id, finalName);
                    record.FileAddress1 = finalName;
                }

                // 4) Generate PDF, print, email (unchanged)
                // 4) Generate PDF, print, email
                var pdfPath = _holdTagService.GenerateHoldTagPdf(record);
                var attachment1Abs = _holdTagService.GetAbsolutePath(record.FileAddress1);

                // how many copies to print = how many hold tags
                _sharedService.PrintFileToSpecificPrinter(_holdTagPrinter, pdfPath, record.Quantity.GetValueOrDefault(1));

                // Build nicer subject + body
                string prodNumber = string.IsNullOrWhiteSpace(record.ProdNumber) ? "N/A" : record.ProdNumber;
                string runNumber = string.IsNullOrWhiteSpace(record.RunNumber) ? "N/A" : record.RunNumber;
                string part = string.IsNullOrWhiteSpace(record.Part) ? "N/A" : record.Part;
                string component = string.IsNullOrWhiteSpace(record.Component) ? "N/A" : record.Component;
                string lotNumber = string.IsNullOrWhiteSpace(record.LotNumber) ? "N/A" : record.LotNumber;
                string materialCode = string.IsNullOrWhiteSpace(record.MaterialCode) ? "N/A" : record.MaterialCode;
                string discrepancy = string.IsNullOrWhiteSpace(record.Discrepancy) ? "N/A" : record.Discrepancy;
                string issuedBy = string.IsNullOrWhiteSpace(record.IssuedBy) ? "Unknown" : record.IssuedBy;
                string issuedDate = record.Date.HasValue ? record.Date.Value.ToString("MM/dd/yyyy") : "N/A";

                string unitDisplay = string.IsNullOrWhiteSpace(record.Unit) ? "" : record.Unit + "(s)";
                string qtyTagsText = record.Quantity.HasValue ? record.Quantity.Value.ToString() : "N/A";
                string qtyOnHoldText = record.QuantityOnHold.HasValue
                    ? $"{record.QuantityOnHold.Value} {unitDisplay}"
                    : "N/A";

                string indexUrl = Url.Action("Index", "HoldTag", null, Request.Scheme);

                // SUBJECT: includes part + prod + run
                string subject = $"HOLD TAG: {part} | Prod {prodNumber} | Run {runNumber}";

                // BODY: nicely formatted summary
                string body = $@"
Parts have been placed on Hold.

Part:        {part}
Component:   {component}
Production:  {prodNumber}
Run:         {runNumber}
Lot #:       {lotNumber}
Material:    {materialCode}
Qty On Hold: {qtyOnHoldText}

Discrepancy
{discrepancy}

Issued
By:   {issuedBy}
Date: {issuedDate}

Links
Hold Tag Page: {indexUrl}
";

                // send email with the hold tag PDF and optional uploaded attachment
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

                
                var ops = await _sharedService.GetAllOperators();
                var records = await _holdTagService.GetAllHoldRecordsAsync();

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

                return RedirectToAction("Admin", "HoldTag");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating HoldRecord: {ex.Message}");
                TempData["ErrorMessage"] = $"An error occurred while updating: {ex.Message}";
                return RedirectToAction("Admin", "HoldTag");
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

        [HttpGet("ScanLookup")]
        public async Task<IActionResult> ScanLookup(string mode, string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return Json(new { success = false, message = "No code provided." });

            mode = (mode ?? "").ToLowerInvariant();
            HoldScanResult? result = null;

            if (mode == "prod")
            {
                result = await _holdTagService.LookupByProdAsync(code.Trim());
            }
            else if (mode == "run")
            {
                result = await _holdTagService.LookupByRunAsync(code.Trim());
            }
            else
            {
                return Json(new { success = false, message = "Invalid mode. Use 'prod' or 'run'." });
            }

            if (result == null)
                return Json(new { success = false, message = $"No records found for {mode} '{code}'." });

            return Json(new
            {
                success = true,
                source = result.Source,
                part = result.Part,
                component = result.Component,
                prodNumber = result.ProdNumber,
                runNumber = result.RunNumber,
                lotNumber = result.LotNumber,
                materialCode = result.MaterialCode
            });
        }

    }
}
