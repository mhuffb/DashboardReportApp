﻿using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class MoldingController : Controller
    {
        private readonly MoldingService _moldingService;

        public MoldingController(MoldingService MoldingService)
        {
            _moldingService = MoldingService;
        }

        [HttpGet]
        public IActionResult Index(string searchTerm, string sortColumn = "Id", bool sortDescending = true)
        {
            var viewModel = _moldingService.GetFilteredData(searchTerm, sortColumn, sortDescending);
            return View(viewModel);
        }



    }
}
