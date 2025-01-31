using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Web;
using System.Web.Mvc;
using IAuthorizationFilter = Microsoft.AspNetCore.Mvc.Filters.IAuthorizationFilter;

namespace DashboardReportApp.Controllers.Attributes
{


    public class PasswordProtectedAttribute : Attribute, IAuthorizationFilter
    {
        private const string PasswordSessionKey = "PasswordProtectedPageAccessGranted";
        public string Password { get; set; } // Set this to the required password


        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var httpContext = context.HttpContext;

            // Check if the user is already authenticated
            if (httpContext.Session.GetString(PasswordSessionKey) == "true")
            {
                return; // Allow access
            }

            // 🚀 Store the originally requested URL before redirecting to login
            if (httpContext.Request.Method == "GET")
            {
                httpContext.Session.SetString("ReturnUrl", httpContext.Request.Path);
            }

            // 🚀 Redirect to `/Admin/PasswordEntry` for authentication
            context.Result = new RedirectToActionResult("PasswordEntry", "Admin", null);
        }

    }
}