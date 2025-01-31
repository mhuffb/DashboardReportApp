using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")] // Set your password here
    [Route("AdminDeviation")]
    public class AdminDeviationController : Controller
    {
        private readonly AdminDeviationService _deviationService;

        public AdminDeviationController(AdminDeviationService deviationService)
        {
            _deviationService = deviationService;
        }

        [HttpGet("List")]
        public IActionResult List()
        {
            List<AdminDeviationModel> deviations = _deviationService.GetAllDeviations();
            return View(deviations);
        }

        [HttpPost("Update")]
        public IActionResult Update(AdminDeviationModel model)
        {
            if (!ModelState.IsValid)
            {
                return RedirectToAction("List");
            }

            _deviationService.UpdateDeviation(model);
            TempData["Success"] = "Deviation updated successfully!";
            return RedirectToAction("List");
        }
    }
}
