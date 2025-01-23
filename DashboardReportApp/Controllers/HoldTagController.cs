using System.Threading.Tasks;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    [Route("[controller]/[action]")]
    public class HoldTagController : Controller
    {
        private readonly HoldTagService _service;

        public HoldTagController(HoldTagService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewData["Operators"] = await _service.GetOperatorsAsync();
            return View(new HoldRecord());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(HoldRecord record)
        {
            if (ModelState.IsValid)
            {
                // Assign default values
                record.Date = DateTime.Now;

                // Generate and set the PDF path (if applicable)
                string pdfPath = _service.GeneratePdf(record);
                record.FileAddress = pdfPath;

                // Save to the database
                await _service.AddHoldRecordAsync(record);

                // Provide success feedback and redirect
                TempData["SuccessMessage"] = "Hold record submitted successfully!";
                return RedirectToAction("Index");
            }
            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState)
                {
                    Console.WriteLine($"Key: {error.Key}");
                    foreach (var subError in error.Value.Errors)
                    {
                        Console.WriteLine($" - ErrorMessage: {subError.ErrorMessage}");
                    }
                }

                TempData["ErrorMessage"] = "Please correct the errors and try again.";
                ViewData["Operators"] = await _service.GetOperatorsAsync();
                return View("Index", record);
            }

            // Reload operators if validation fails
            ViewData["Operators"] = await _service.GetOperatorsAsync();
            TempData["ErrorMessage"] = "Please correct the errors and try again.";
            return View("Index", record);
        }

    }

}
