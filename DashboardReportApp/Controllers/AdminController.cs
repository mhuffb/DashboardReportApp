using DashboardReportApp.Controllers.Attributes;
using Microsoft.AspNetCore.Mvc;
using MySqlX.XDevAPI;

namespace DashboardReportApp.Controllers
{
    [Route("Admin")]
    public class AdminController : Controller
    {
        private const string PasswordSessionKey = "PasswordProtectedPageAccessGranted";

        [Route("")]
        public IActionResult Index()
        {
            return View();
        }
        [HttpGet("PasswordEntry")]
        public IActionResult PasswordEntry()
        {
            ViewBag.ErrorMessage = TempData["ErrorMessage"];
            return View();
        }

        [HttpPost("PasswordEntry")]
        public IActionResult PasswordEntry(string password)
        {
            if (password == "5intergy") // Replace with actual password
            {
                HttpContext.Session.SetString(PasswordSessionKey, "true");

                // 🚀 Redirect back to the original page if available
                var returnUrl = HttpContext.Session.GetString("ReturnUrl");
                if (!string.IsNullOrEmpty(returnUrl))
                {
                    HttpContext.Session.Remove("ReturnUrl");
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Index", "Home"); // Default fallback redirect
            }

            TempData["ErrorMessage"] = "Invalid password.";
            return RedirectToAction("PasswordEntry");
        }

        [HttpGet("Logout")]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("PasswordEntry");
        }

    }
}
