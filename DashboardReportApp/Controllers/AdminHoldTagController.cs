using DashboardReportApp.Controllers.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")]
    [Route("AdminHoldTag")]
    public class AdminHoldTagController : Controller
    {
        // If anything still hits /AdminHoldTag/Index, send them to the new admin page
        [HttpGet("Index")]
        public IActionResult Index()
        {
            return RedirectToAction("Admin", "HoldTag");
        }
    }
}
