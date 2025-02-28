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

        public MaintenanceRequestController( MaintenanceRequestService service)
        {
            _service = service;
        }


        [HttpPost("SaveImagePath")]
        public async Task<IActionResult> SaveImagePath(int id, string imagePath)
        {
            bool success = await _service.UpdateFile1Link(id, imagePath);
            if (success)
                TempData["Success"] = "Image path updated successfully.";
            else
                TempData["Error"] = "Failed to update the image path. Please try again.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            // Use GetAllRequests() to return every maintenance request (open and closed)
            var requests = _service.GetAllRequests();

            // Populate Requesters and EquipmentList as before
            ViewData["Requesters"] = await _service.GetRequestersAsync();
            ViewData["EquipmentList"] = await _service.GetEquipmentListAsync();
            return View(requests);
        }


        [HttpPost("AddRequest")]
        public async Task<IActionResult> AddRequest(
            [FromForm] MaintenanceRequestModel request,
            [FromForm] IFormFile? file
        )
        {
            // Log the start of the action
            Console.WriteLine("[DEBUG] AddRequest method invoked.");

            // Log the received data (basic fields)
            Console.WriteLine($"[DEBUG] request.Equipment: {request.Equipment}");
            Console.WriteLine($"[DEBUG] request.Requester: {request.Requester}");
            Console.WriteLine($"[DEBUG] request.Problem: {request.Problem}");
            Console.WriteLine($"[DEBUG] request.Department: {request.Department}");
            Console.WriteLine($"[DEBUG] request.Status: {request.Status}");
            Console.WriteLine($"[DEBUG] File upload present? {(file != null ? "Yes" : "No")}");

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
                // Log DownStatus
                Console.WriteLine($"[DEBUG] request.DownStatus: {request.DownStatus}");

                // Set default values
                if (request.DownStatus == true)
                {
                    Console.WriteLine("[DEBUG] DownStatus == true; setting DownStartDateTime to now.");
                    request.DownStartDateTime = DateTime.Now;
                }
                request.Status = "Open";

                // First, add the new request without the file.
                Console.WriteLine("[DEBUG] About to call AddRequestAsync...");
                bool insertSuccess = await _service.AddRequestAsync(request);
                Console.WriteLine($"[DEBUG] Insert success? {insertSuccess}");

                if (!insertSuccess)
                {
                    TempData["Error"] = "Failed to add the request.";
                    Console.WriteLine("[ERROR] Insert returned false; returning RedirectToAction(Index).");
                    return RedirectToAction(nameof(Index));
                    // Alternatively: return BadRequest(...) if you prefer consistent JSON
                }

                // Now request.Id should be populated.
                Console.WriteLine($"[DEBUG] Request inserted with ID: {request.Id}");

                // If a file was uploaded, handle the file upload.
                if (file != null && file.Length > 0)
                {
                    Console.WriteLine("[DEBUG] Handling file upload...");
                    var uploadsFolder = @"\\SINTERGYDC2024\Vol1\VSP\Uploads";
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Console.WriteLine("[DEBUG] Upload folder does not exist, creating it...");
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var fileExtension = Path.GetExtension(file.FileName);
                    var fileName = $"MaintenanceRequestFile1_{request.Id}{fileExtension}";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    Console.WriteLine($"[DEBUG] Saving file at: {filePath}");

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(fileStream);
                    }
                    Console.WriteLine("[DEBUG] File uploaded successfully.");

                    // Update the record with the new file path.
                    Console.WriteLine("[DEBUG] Updating DB record with file path...");
                    bool fileUpdateSuccess = await _service.UpdateFile1Link(request.Id, filePath);
                    Console.WriteLine($"[DEBUG] File update success? {fileUpdateSuccess}");
                    if (!fileUpdateSuccess)
                    {
                        TempData["Error"] = "Failed to update the image link.";
                        Console.WriteLine("[ERROR] Could not update MaintenanceRequestFile1 link in DB.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception in AddRequest: {ex.Message}");
                TempData["Error"] = "An error occurred while adding the request.";
            }

            Console.WriteLine("[DEBUG] Returning success JSON: success=true, message=Request added successfully!");
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
                Console.WriteLine($"[ERROR] FetchImage exception: {ex}");
                // Return an appropriate error response
                return StatusCode(500, new { success = false, message = ex.Message });
            }
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

        [HttpPost("UpdateFileAddress1")]
        public async Task<IActionResult> UpdateFileAddress1(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a valid file.";
                Console.WriteLine("[ERROR] No file selected or file is empty.");
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
                var fileName = $"MaintenanceRequestDoc_{id}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                Console.WriteLine($"[DEBUG] Saving file at: {filePath}");

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                Console.WriteLine("[DEBUG] File saved successfully.");

                // Ensure NULL safety when updating the database
                bool success = await _service.UpdateFile1Link(id, filePath ?? "");

                if (success)
                {
                    TempData["Success"] = "File uploaded and link updated successfully.";
                }
                else
                {
                    TempData["Error"] = "Failed to update the image link.";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] File upload failed: {ex.Message}");
                TempData["Error"] = "An error occurred while uploading the file.";
            }

            return RedirectToAction(nameof(Index));
        }


    }

}