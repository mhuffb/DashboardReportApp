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
        private readonly MaintenanceRequestService _maintenanceService;

        public MaintenanceRequestController( MaintenanceRequestService service)
        {
            _maintenanceService = service;
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
            [FromForm] IFormFile? file
        )
        {

            // Validate the model state
            if (!ModelState.IsValid)
            {
                Console.WriteLine("[DEBUG] ModelState is invalid. Errors:");
                foreach (var error in ModelState)
                {
                    Console.WriteLine($"Key: {error.Key}");
                    foreach (var subError in error.Value.Errors)
                    {
                        Console.WriteLine($"  Error: {subError.ErrorMessage}");
                    }
                }
                TempData["Error"] = "Invalid input. Please correct the errors.";
                return BadRequest(new { success = false, message = "Validation failed." });
            }

            try
            {

                // Set default values
                if (request.DownStatus == true)
                {
                    request.DownStartDateTime = DateTime.Now;
                }
                request.Status = "Open";



                /* 2️⃣  If there’s a picture, save it **first** and store its path   */
                if (file is { Length: > 0 })
                {
                    var uploadsFolder = @"\\SINTERGYDC2024\Vol1\VSP\Uploads";
                    Directory.CreateDirectory(uploadsFolder);                // makes folder if missing

                    // Use a GUID so the name is unique even before we know the ID
                    var ext = Path.GetExtension(file.FileName);
                    var tempName = $"MaintenanceRequestFile1_{Guid.NewGuid()}{ext}";
                    var tempPath = Path.Combine(uploadsFolder, tempName);

                    await using var fs = new FileStream(tempPath, FileMode.Create);
                    await file.CopyToAsync(fs);

                    request.FileAddress1 = tempPath;                         // ⬅ key line
                }

                int id = await _maintenanceService.AddRequestAsync(request);


                // If a file was uploaded, handle the file upload.
                if (file != null && file.Length > 0)
                {
                    UpdateFileAddress1(id, file);

                   
                }

            }
            catch (Exception ex)
            {
                TempData["Error"] = "An error occurred while adding the request.";
            }

            return Ok(new { success = true, message = "Request added successfully!" });
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
                // Return an appropriate error response
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

       
        [HttpPost("UpdateFileAddress1")]
        public async Task<IActionResult> UpdateFileAddress1(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a valid file.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var uploadsFolder = @"\\SINTERGYDC2024\Vol1\VSP\Uploads";
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var fileExtension = Path.GetExtension(file.FileName);
                var fileName = $"MaintenanceRequestFile1_{id}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName);


                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }


                // Ensure NULL safety when updating the database
                string filePathImage = await _maintenanceService.UpdateFile1Link(id, filePath ?? "");

            }
            catch (Exception ex)
            {
                TempData["Error"] = "An error occurred while uploading the file.";
            }

            return RedirectToAction(nameof(Index));
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