using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    public class QCController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
