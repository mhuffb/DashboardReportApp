using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
