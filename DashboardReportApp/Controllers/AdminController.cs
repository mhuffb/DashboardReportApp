using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class AdminController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
