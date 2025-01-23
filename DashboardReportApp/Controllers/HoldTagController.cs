using System.Threading.Tasks;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class HoldTagController : Controller
    {
        private readonly HoldTagService _service;

        public HoldTagController(HoldTagService service)
        {
            _service = service;
        }

        public async Task<IActionResult> Index()
        {
            ViewData["Operators"] = await _service.GetOperatorsAsync();
            return View(new HoldRecord());
        }

        [HttpPost]
        public async Task<IActionResult> Submit(HoldRecord record)
        {
            if (ModelState.IsValid)
            {
                record.Date = DateTime.Now;
                string pdfPath = _service.GeneratePdf(record);
                record.FileAddress = pdfPath;

                await _service.AddHoldRecordAsync(record);

                return RedirectToAction("Index");
            }

            ViewData["Operators"] = await _service.GetOperatorsAsync();
            return View("Index", record);
        }
    }
}
