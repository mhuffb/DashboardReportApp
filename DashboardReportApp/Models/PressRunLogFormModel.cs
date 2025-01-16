namespace DashboardReportApp.Models
{
    using System;

    public class PressRunLogFormModel
    {
        // Login properties
        public string Operator { get; set; }
        public string Part { get; set; }
        public string Machine { get; set; }
        public DateTime StartDateTime { get; set; } // Required for login

        // Logout properties
        public int? Scrap { get; set; } // Required for logout
        public DateTime? EndDateTime { get; set; } // Required for logout
        public string Notes { get; set; } // Optional for logout
    }



}
