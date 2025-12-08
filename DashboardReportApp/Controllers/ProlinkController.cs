using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System;

namespace DashboardReportApp.Controllers
{
    [Route("Prolink")]
    public class ProlinkController : Controller
    {
        private readonly ProlinkService _prolinkService;

        public ProlinkController(ProlinkService prolinkService)
        {
            _prolinkService = prolinkService;
        }

        [HttpGet("")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet("GeneratePdf")]
        public IActionResult GeneratePdf(
            string partString,
            string type,
            DateTime? startDate,
            DateTime? endDate,
            bool onlyOutOfSpec = false,
            bool includeCorrectiveActions = false)
        {
            try
            {
                byte[] pdfBytes = _prolinkService.GeneratePdf(
                    partString,
                    type,
                    startDate,
                    endDate,
                    onlyOutOfSpec,
                    includeCorrectiveActions
                );
                return File(pdfBytes, "application/pdf", "report.pdf");
            }
            catch (Exception ex)
            {
                // TEMP: send full exception to client to debug
                return StatusCode(500, ex.ToString());
            }

        }

        [HttpGet("QueryData")]
        public IActionResult QueryData(
            string partString,
            string type,
            DateTime? startDate,
            DateTime? endDate,
            bool onlyOutOfSpec = false,
            bool includeCorrectiveActions = false)
        {
            try
            {
                var pivotedResults = _prolinkService.GetPivotedData(
                    partString,
                    type,
                    startDate,
                    endDate,
                    onlyOutOfSpec,
                    includeCorrectiveActions
                );
                return Json(new
                {
                    departmentResults = pivotedResults
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}