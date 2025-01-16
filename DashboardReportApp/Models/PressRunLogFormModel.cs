namespace DashboardReportApp.Models
{
    using System;

    public class PressRunLogFormModel
    {
        public string Operator { get; set; }
        public string Part { get; set; }
        public string Machine { get; set; }
        public string Run { get; set; }
        public int? PcsStart { get; set; }
        public int? PcsEnd { get; set; }
        public int? Scrap { get; set; }
        public string Notes { get; set; }
        public DateTime StartDateTime { get; set; } // For login time
        public DateTime EndDateTime { get; set; }   // For logout time
    }

}
