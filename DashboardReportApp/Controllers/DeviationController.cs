using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    public class DeviationController : Controller
    {
        private readonly DeviationService _service;

        public DeviationController(DeviationService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Get operators for the dropdown (synchronously in this example)
            ViewData["Operators"] = _service.GetOperators();

            // Retrieve all deviation records asynchronously
            var records = await _service.GetAllDeviationsAsync();

            // Create a composite view model
            var model = new DeviationIndexViewModel
            {
                FormModel = new DeviationModel(), // New instance for the form
                Records = records
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DeviationModel model)
        {
            if (ModelState.IsValid)
            {
                // Save the new deviation (asynchronously)
                await _service.SaveDeviationAsync(model);

                // Generate and print PDF
                string pdfPath = _service.GenerateAndPrintDeviationPdf(model);

                TempData["SuccessMessage"] = "Deviation successfully created!";
                return RedirectToAction("Index");
            }

            // Log errors if any (for debugging)
            foreach (var key in ModelState.Keys)
            {
                foreach (var error in ModelState[key].Errors)
                {
                    System.Console.WriteLine($"Key: {key}, Error: {error.ErrorMessage}");
                }
            }

            TempData["ErrorMessage"] = "Please correct the errors and try again.";
            ViewData["Operators"] = _service.GetOperators();
            var records = await _service.GetAllDeviationsAsync();
            var viewModel = new DeviationIndexViewModel
            {
                FormModel = model,
                Records = records
            };
            return View("Index", viewModel);
        }
    }
}
