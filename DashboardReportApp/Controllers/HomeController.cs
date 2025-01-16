using System.Diagnostics;
using DashboardReportApp.Models;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            string picoDataApiLink = Url.ActionLink("HandlePut", "PicoData", null, Request.Scheme);
            ViewData["PicoDataApiLink"] = picoDataApiLink;

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
