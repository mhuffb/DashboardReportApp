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

            // Check if the user has already entered the correct password
            if (httpContext.Session.GetString(PasswordSessionKey) == "true")
            {
                return; // Allow access
            }

            // Check if the user is submitting the password form
            if (httpContext.Request.Method == "POST")
            {
                var enteredPassword = httpContext.Request.Form["password"].ToString();
                if (enteredPassword == Password)
                {
                    httpContext.Session.SetString(PasswordSessionKey, "true");
                    return; // Allow access
                }
            }

            // Redirect to the password entry page if unauthorized
            context.Result = new Microsoft.AspNetCore.Mvc.ViewResult { ViewName = "PasswordEntry" };
        }
    }
}