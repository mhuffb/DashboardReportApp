using System.Threading.Tasks;
using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")] // Set your password here
    [Route("AdminHoldTag")]
    public class AdminHoldTagController : Controller
    {
        private readonly AdminHoldTagService _holdtagservice;

        public AdminHoldTagController(AdminHoldTagService service)
        {
            _holdtagservice = service;
        }



        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            List<AdminHoldTagModel> records = await _holdtagservice.GetAllHoldRecordsAsync();

            // Get operator lists for the dropdowns
            ViewBag.IssuedByOperators = await _holdtagservice.GetIssuedByOperatorsAsync();
            ViewBag.DispositionOperators = await _holdtagservice.GetDispositionOperatorsAsync();
            ViewBag.ReworkOperators = await _holdtagservice.GetReworkOperatorsAsync();

            return View(records);
        }






        [HttpPost("UpdateRequest")]
        public IActionResult UpdateRequest(AdminHoldTagModel model, IFormFile? FileUpload)
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

                return View("AdminView", _holdtagservice.GetAllHoldRecordsAsync());
            }

            try
            {
                // Call the service to update the request
                _holdtagservice.UpdateRequest(model, FileUpload);

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return View("Index", _holdtagservice.GetAllHoldRecordsAsync());
            }
        }
    }

}
