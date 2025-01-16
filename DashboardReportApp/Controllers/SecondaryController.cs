using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class SecondaryController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
