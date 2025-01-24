using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

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
        public IActionResult Index()
        {
            ViewData["Operators"] = _service.GetOperators();
            return View();
        }

        

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(DeviationViewModel model)
        {
            if (ModelState.IsValid)
            {
                _service.SaveDeviation(model);

                // Generate the PDF
                string pdfPath = _service.GenerateAndPrintDeviationPdf(model);

                TempData["SuccessMessage"] = "Deviation successfully created!";
                return RedirectToAction("Index");
            }

            // Log validation errors
            foreach (var key in ModelState.Keys)
            {
                var errors = ModelState[key].Errors;
                foreach (var error in errors)
                {
                    Console.WriteLine($"Key: {key}, Error: {error.ErrorMessage}");
                }
            }

            TempData["ErrorMessage"] = "Please correct the errors and try again.";
            ViewData["Operators"] = _service.GetOperators();
            return View("Index", model); // Ensure the view name is correct
        }


    }
}
