using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    [Route("[controller]")]
    public class DeviationController : Controller
    {
        private readonly DeviationService _service;
        public DeviationController(DeviationService service) => _service = service;

        // GET /Deviation
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            ViewData["Operators"] = _service.GetOperators();
            var records = await _service.GetAllDeviationsAsync();
            var model = new DeviationIndexViewModel { FormModel = new DeviationModel(), Records = records };
            return View(model);
        }

        // POST /Deviation/Create
        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DeviationModel model, IFormFile? file)
        {
            if (ModelState.IsValid)
            {
                await _service.SaveDeviationAsync(model, file);
                var pdfPath = _service.GenerateAndPrintDeviationPdf(model); // keep your side effect
                TempData["SuccessMessage"] = "Deviation successfully created!";
                return RedirectToAction(nameof(Index));
            }

            foreach (var key in ModelState.Keys)
                foreach (var error in ModelState[key].Errors)
                    System.Console.WriteLine($"Key: {key}, Error: {error.ErrorMessage}");

            TempData["ErrorMessage"] = "Please correct the errors and try again.";
            ViewData["Operators"] = _service.GetOperators();
            var records = await _service.GetAllDeviationsAsync();
            return View("Index", new DeviationIndexViewModel { FormModel = model, Records = records });
        }

        // POST /Deviation/UpdateFile
        [HttpPost("UpdateFile")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateFile(int id, IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "Please select a valid file.";
                return RedirectToAction(nameof(Index));
            }

            bool success = await _service.UpdateFileAddress1Async(id, file);
            TempData["SuccessMessage"] = success ? "File updated successfully!" : "Update failed.";
            return RedirectToAction(nameof(Index));
        }

        // GET /Deviation/FetchFile?name=...
        [HttpGet("FetchFile")]
        public IActionResult FetchFile(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Json(new { success = false, message = "No file name provided." });

            var abs = _service.GetAbsolutePath(name);
            if (string.IsNullOrEmpty(abs) || !System.IO.File.Exists(abs))
                return Json(new { success = false, message = $"File not found: {name}" });

            var url = Url.Action(nameof(StreamFile), "Deviation", new { name }, Request.Scheme);
            return Json(new { success = true, url });
        }

        // GET /Deviation/StreamFile?name=...
        [HttpGet("StreamFile")]
        public IActionResult StreamFile(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return NotFound();
                var abs = _service.GetAbsolutePath(name);
                if (string.IsNullOrEmpty(abs) || !System.IO.File.Exists(abs)) return NotFound();

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

