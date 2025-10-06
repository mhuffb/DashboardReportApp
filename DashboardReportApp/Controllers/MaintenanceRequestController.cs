using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    using DashboardReportApp.Models;
    using DashboardReportApp.Services;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;
    using System.Threading.Tasks;
    [Route("MaintenanceRequest")]
    public class MaintenanceRequestController : Controller
    {
        private readonly MaintenanceRequestService _maintenanceService;
        private readonly PathOptions _paths;

        public MaintenanceRequestController(
            MaintenanceRequestService service,
            IOptions<PathOptions> pathOptions)
        {
            _maintenanceService = service;
            _paths = pathOptions.Value;
        }


        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            // Use GetAllRequests() to return every maintenance request (open and closed)
            var requests = _maintenanceService.GetAllRequests();

            // Populate Requesters and EquipmentList as before
            ViewData["Requesters"] = await _maintenanceService.GetRequestersAsync();
            ViewData["EquipmentList"] = await _maintenanceService.GetEquipmentListAsync();
            return View(requests);
        }


        [HttpPost("AddRequest")]
        public async Task<IActionResult> AddRequest(
       [FromForm] MaintenanceRequestModel request,
       [FromForm] IFormFile? file)
        {
            // 1) Basic validation
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, message = "Validation failed." });
            }

            try
            {
                // 2) Defaults
                if (request.DownStatus == true)
                    request.DownStartDateTime = DateTime.Now;
                request.Status = "Open";

                // 3) If a file was uploaded, save it to the configured folder and store only the FILENAME
                if (file is { Length: > 0 })
                {
                    // Use your configured maintenance uploads folder
                    var uploadsFolder = _maintenanceService.Paths.MaintenanceUploads; // expose via a property or pass PathOptions into the controller
                    Directory.CreateDirectory(uploadsFolder);

                    var ext = Path.GetExtension(file.FileName);
                    // Use a temporary unique name right now; we'll rename after we get the DB Id if you prefer
                    var tempName = $"MaintenanceRequestFile1_{Guid.NewGuid()}{ext}";
                    var tempFullPath = Path.Combine(uploadsFolder, tempName);

                    await using (var fs = new FileStream(tempFullPath, FileMode.Create))
                    {
                        await file.CopyToAsync(fs);
                    }

                    // Store only the filename in the model (per your new rule)
                    request.FileAddress1 = tempName;
                }

                // 4) Insert into DB (service should generate PDF, email, print — and internally swallow non-critical errors)
                var id = await _maintenanceService.AddRequestAsync(request);

                // 5) Optional: if you want the uploaded file to be renamed to the final "{id}" pattern, do it here
                if (file is { Length: > 0 } && !string.IsNullOrWhiteSpace(request.FileAddress1))
                {
                    try
                    {
                        var uploadsFolder = _maintenanceService.Paths.MaintenanceUploads;
                        var oldName = request.FileAddress1!;
                        var oldFull = Path.Combine(uploadsFolder, oldName);
                        var ext = Path.GetExtension(oldName);

                        var finalName = $"MaintenanceRequestFile1_{id}{ext}";
                        var finalFull = Path.Combine(uploadsFolder, finalName);

                        if (System.IO.File.Exists(finalFull))
                            System.IO.File.Delete(finalFull);

                        System.IO.File.Move(oldFull, finalFull);

                        // persist the new filename (not full path)
                        await _maintenanceService.UpdateFile1Link(id, finalName);
                    }
                    catch (Exception exRename)
                    {
                        // Non-critical: keep request successful, just note it
                        Console.WriteLine($"[WARN] Rename after insert failed: {exRename.Message}");
                    }
                }

                // 6) All good
                return Ok(new { success = true, message = "Request added successfully!", id });
            }
            catch (Exception ex)
            {
                // Only DB/insert errors should reach here
                Console.WriteLine($"[ERROR] AddRequest failed: {ex}");
                return StatusCode(500, new { success = false, message = "Server error while adding request." });
            }
        }


        // keep this ONLY for direct posts from the UI, don’t call it from AddRequest
        [HttpPost("UpdateFileAddress1")]
        public async Task<IActionResult> UpdateFileAddress1(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "No file received." });

            try
            {
                var uploadsFolder = _maintenanceService.Paths.MaintenanceUploads;
                Directory.CreateDirectory(uploadsFolder);

                var ext = Path.GetExtension(file.FileName);
                var fileName = $"MaintenanceRequestFile1_{id}{ext}";
                var fullPath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                    await file.CopyToAsync(stream);

                await _maintenanceService.UpdateFile1Link(id, fileName); // store filename only
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] UpdateFileAddress1: {ex}");
                return StatusCode(500, new { success = false, message = "Failed to upload file." });
            }
        }


        // returns a JSON with a URL to stream the file (no copies to wwwroot)
        [HttpGet("FetchImage")]
        public IActionResult FetchImage(string filePath)
        {
            var fileName = Path.GetFileName(filePath ?? "");
            if (string.IsNullOrWhiteSpace(fileName))
                return Json(new { success = false, message = "No file provided." });

            var url = Url.Action("Preview", "MaintenanceRequest", new { file = fileName }, Request.Scheme);
            return Json(new { success = true, url });
        }

        // streams the content directly from MaintenanceUploads
        // streams the content directly from MaintenanceUploads
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

        // New endpoint to return all open maintenance requests for  molding dashboard
        [HttpGet("ApiGetAllOpenRequests")]
        public async Task<IActionResult> ApiGetAllOpenRequests()
        {
            try
            {
                var requests = await _maintenanceService.GetAllOpenRequestsAsync();
                return Ok(requests);
            }
            catch (Exception ex)
            {
                // Optionally log the exception here
                return StatusCode(500, "Internal server error");
            }
        }
    }

}