using DashboardReportApp.Controllers.Attributes;
using Microsoft.AspNetCore.Mvc;
using MySqlX.XDevAPI;

namespace DashboardReportApp.Controllers
{
    [Route("Admin")]
    public class AdminController : Controller
    {
        [Route("")]
        public IActionResult Index()
        {
            return View();
        }
        [Route("PasswordEntry")]
        public ActionResult PasswordEntry()
        {
            ViewBag.ErrorMessage = TempData["ErrorMessage"];
            return View();
        }
        [Route("Logout")]
        public ActionResult Logout()
        {
            // Remove all session keys
            foreach (var key in HttpContext.Session.Keys)
            {
                HttpContext.Session.Remove(key);
            }

            // Optionally, you can also clear the session cookie
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home"); // Redirect to the home page
        }
        [Route("ProtectedView")] // Define the route for this action
        public IActionResult ProtectedView()
        {
            return View();
        }
    }
}
