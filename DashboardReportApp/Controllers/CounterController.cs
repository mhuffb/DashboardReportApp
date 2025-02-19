using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class CounterController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
