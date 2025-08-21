using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace DashboardReportApp.Controllers
{
    [Route("AdminProcessChangeRequest")]
    [PasswordProtected(Password = "5intergy")] // Set your password here
    public class AdminProcessChangeRequestController : Controller
    {
        private readonly AdminProcessChangeRequestService _service;
        private readonly SharedService _sharedService;
        public AdminProcessChangeRequestController(AdminProcessChangeRequestService service, SharedService sharedService)
        {
            _service = service;
            _sharedService = sharedService;
        }

        //  public IActionResult Index()
        // {
        //     var requests = _service.GetAllRequests();
        //     return View(requests);
        //  }
        [Route("Index")]
        public IActionResult Index()
        {
            // Load operator names from the DB
            // e.g. _operatorService.GetAllOperatorNames() => List<string>
            var operators = _service.GetOperators();
            ViewBag.OperatorNames = operators;

            var requests = _service.GetAllRequests();
            return View(requests);
        }



        [HttpPost("UpdateRequest")]
        public IActionResult UpdateRequest(ProcessChangeRequestModel model, IFormFile? FileUpload1, IFormFile? FileUpload2)
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
                return View("Index", _service.GetAllRequests());
            }

            try
            {
                // Call the service to update the request with both file uploads
                _service.UpdateRequest(model, FileUpload1, FileUpload2);
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return View("Index", _service.GetAllRequests());
            }
        }


        [HttpPost("UpdateFileAddress1")]
        public IActionResult UpdateFileAddress1(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a valid file.";
                Console.WriteLine("Error: No file selected or file is empty.");
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Ensure the uploads folder exists
                var uploadsFolder = @"\\SINTERGYDC2024\Vol1\Visual Studio Programs\VSP\Uploads";
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                    Console.WriteLine($"Created uploads folder at: {uploadsFolder}");
                }

                // Construct the file name and paths
                var fileExtension = Path.GetExtension(file.FileName);
                var fileName = $"ProcessChangeRequestFile1_{id}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName); // Physical path

                Console.WriteLine($"File details: Name = {fileName}, Extension = {fileExtension}");
                Console.WriteLine($"Physical path: {filePath}");

                // Save the file to the physical path
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }
                Console.WriteLine($"File successfully saved at: {filePath}");

                // Update the database with the relative path
                var request = _service.GetAllRequests().FirstOrDefault(r => r.Id == id);
                if (request != null)
                {
                    request.FileAddress1 = filePath; // Use relative path
                    _service.UpdateFileAddress(id, filePath);
                    Console.WriteLine($"Database updated for Request ID = {id}, FileAddress1 = {filePath}");
                }
                else
                {
                    Console.WriteLine($"Request with ID = {id} not found.");
                }

                TempData["Success"] = "File uploaded and link updated successfully.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                TempData["Error"] = $"An error occurred: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost("UpdateFileAddress2")]
        public IActionResult UpdateFileAddress2(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a valid file.";
                Console.WriteLine("Error: No file selected or file is empty.");
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Ensure the uploads folder exists
                var uploadsFolder = @"\\SINTERGYDC2024\Vol1\Visual Studio Programs\VSP\Uploads";
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                    Console.WriteLine($"Created uploads folder at: {uploadsFolder}");
                }

                // Construct the file name and paths
                var fileExtension = Path.GetExtension(file.FileName);
                var fileName = $"ProcessChangeRequestFile2_{id}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName); // Physical path

                Console.WriteLine($"File details: Name = {fileName}, Extension = {fileExtension}");
                Console.WriteLine($"Physical path: {filePath}");

                // Save the file to the physical path
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }
                Console.WriteLine($"File successfully saved at: {filePath}");

                // Update the database with the relative path
                var request = _service.GetAllRequests().FirstOrDefault(r => r.Id == id);
                if (request != null)
                {
                    request.FileAddress1 = filePath; // Use relative path
                    _service.UpdateFileAddress(id, filePath);
                    Console.WriteLine($"Database updated for Request ID = {id}, FileAddress2 = {filePath}");
                }
                else
                {
                    Console.WriteLine($"Request with ID = {id} not found.");
                }

                TempData["Success"] = "File uploaded and link updated successfully.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                TempData["Error"] = $"An error occurred: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
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
        [HttpGet("SendProlinkNotification")]
        public IActionResult SendProlinkNotification(int id, string part, string request, string requester, string reqdate, string updatedby, string updateresult)

        {
            try
            {
                string toAddress = "mhuff@sintergy.net";
                string subject = $"Update Prolink: Part {part} (Req #{id})";
                string body =
    $"Please update Prolink for the following:\n\n" +
    $"Part: {part}\n" +
    $"Request: {request}\n" +
    $"Requester: {requester}\n" +
    $"Request Date: {reqdate}\n" +
    $"Updated By: {updatedby ?? "N/A"}\n" +
    $"Update Result: {updateresult ?? "N/A"}";


                Console.WriteLine("DEBUG: sending to " + toAddress);
                _sharedService.SendEmailWithAttachment(toAddress, null, null, subject, body);

                return Ok("Sent");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500);
            }
        }

    }
}
