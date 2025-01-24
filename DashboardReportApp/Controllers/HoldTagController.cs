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
            var parts = await _service.GetPartsAsync();
            var operators = await _service.GetOperatorsAsync();

            ViewData["Parts"] = parts ?? new List<string>();
            ViewData["Operators"] = operators;

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
                string pdfPath = _service.GenerateHoldTagPdf(record);
                record.FileAddress = pdfPath;

                // Save to the database
                await _service.AddHoldRecordAsync(record);

                // Print the PDF
                try
                {
                    _service.PrintPdf(pdfPath);
                    TempData["SuccessMessage"] = "Hold record submitted, email sent, and printed successfully!";
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Hold record submitted but failed to print: {ex.Message}";
                }

                // Send email with the generated PDF
                try
                {
                    _service.SendEmailWithAttachment(
                        "notifications@sintergy.net",
                        "$inT15851",
                        "holdtag@sintergy.net",
                        "smtp.sintergy.net",
                        pdfPath,
                        record
                    );
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Failed to send email: {ex.Message}";
                    return RedirectToAction("Index");
                }

                // Provide success feedback and redirect
                TempData["SuccessMessage"] = "Hold record submitted and email sent successfully!";
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

        public async Task<IActionResult> Create()
        {
            // Fetch parts and operators
            var parts = await _service.GetPartsAsync();
            var operators = await _service.GetOperatorsAsync();

            // Pass data to the view
            ViewData["Parts"] = parts;
            ViewData["Operators"] = operators;

            return View();
        }

    }

}
