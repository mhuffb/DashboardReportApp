using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Http;
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
            ViewData["Operators"] = _service.GetOperators();
            var records = await _service.GetAllDeviationsAsync();

            var model = new DeviationIndexViewModel
            {
                FormModel = new DeviationModel(), // New instance for the create form
                Records = records
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DeviationModel model, IFormFile? file)
        {
            if (ModelState.IsValid)
            {
                await _service.SaveDeviationAsync(model, file);
                string pdfPath = _service.GenerateAndPrintDeviationPdf(model);
                TempData["SuccessMessage"] = "Deviation successfully created!";
                return RedirectToAction("Index");
            }

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

        // New action to update FileAddress1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateFile(int id, IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "Please select a valid file.";
                return RedirectToAction("Index");
            }

            bool success = await _service.UpdateFileAddress1Async(id, file);
            TempData["SuccessMessage"] = success ? "File updated successfully!" : "Update failed.";
            return RedirectToAction("Index");
        }
    }
}
