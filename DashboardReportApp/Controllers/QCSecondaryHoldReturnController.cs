using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    public class QCSecondaryHoldReturnController : Controller
    {
        private readonly QCSecondaryHoldReturnService _service;

        public QCSecondaryHoldReturnController(QCSecondaryHoldReturnService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var operators = await _service.GetOperatorsAsync();
            ViewData["Operators"] = operators;
            return View(new QCSecondaryHoldReturnModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(QCSecondaryHoldReturnModel model)
        {
            if (ModelState.IsValid)
            {
                await _service.AddHoldReturnAsync(model);
                TempData["SuccessMessage"] = "Hold return submitted successfully!";
                return RedirectToAction("Index");
            }

            TempData["ErrorMessage"] = "Please correct the errors and try again.";
            ViewData["Operators"] = await _service.GetOperatorsAsync();
            return View("Index", model);
        }

    }
}
