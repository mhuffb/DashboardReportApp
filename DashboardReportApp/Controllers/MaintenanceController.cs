using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class MaintenanceController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
