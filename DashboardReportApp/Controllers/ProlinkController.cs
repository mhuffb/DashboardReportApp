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
        public IActionResult GeneratePdf(string partString, string type, DateTime? startDate, DateTime? endDate)
        {
            try
            {
                byte[] pdfBytes = _prolinkService.GeneratePdf(partString, type, startDate, endDate);
                return File(pdfBytes, "application/pdf", "report.pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("QueryData")]
        public IActionResult QueryData(string partString, string type, DateTime? startDate, DateTime? endDate, bool onlyOutOfSpec = false)
        {
            try
            {
                var records = _prolinkService.GetMeasurementRecords(partString, type, startDate, endDate);
                if (onlyOutOfSpec)
                {
                    // Filter records where the measurement is out of spec.
                    records = records.Where(r =>
                    {
                        double measurement;
                        if (double.TryParse(r.MeasurementValue, out measurement))
                        {
                            double usl = r.Nominal + r.TolPlus;
                            double lsl = r.Nominal + r.TolMinus; // LSL computed as nominal plus tolMinus
                            return measurement > usl || measurement < lsl;
                        }
                        return false;
                    }).ToList();
                }
                return Json(records);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

    }
}
