using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    [Route("Molding")]
    public class MoldingController : Controller
    {
        private readonly MoldingService _moldingService;
        private readonly SharedService _sharedService;

        public MoldingController(MoldingService moldingService, SharedService sharedService)
        {
            _moldingService = moldingService;
            _sharedService = sharedService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var viewModel = _moldingService.GetData();
            return View(viewModel);
        }

        [Route("ApiGetAllMachineCounts")]
        [HttpGet]
        public async Task<IActionResult> ApiGetAllMachineCounts()
        {
            Dictionary<string, int?> allCounts = await _moldingService.GetAllMachineCountsAsync();
            return Json(allCounts);
        }
        [Route("ApiGetLatestPart")]
        [HttpGet]
        public async Task<IActionResult> ApiGetLatestPart([FromQuery] string machine)
        {
            if (string.IsNullOrWhiteSpace(machine))
            {
                return BadRequest("Machine parameter is required.");
            }

            string partName = await _sharedService.GetLatestProlinkPartForMachineAsync(machine);
            if (string.IsNullOrEmpty(partName))
            {
                return NotFound("No part found for the specified machine in the last hour.");
            }

            return Json(new { partName });
        }

        // New endpoint to get inspection data for a given part.
        [HttpGet("GetInspectionData")]
        public IActionResult GetInspectionData(string partNumber)
        {
            try
            {
                var data = _moldingService.GetInspectionData(partNumber);
                return Json(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
