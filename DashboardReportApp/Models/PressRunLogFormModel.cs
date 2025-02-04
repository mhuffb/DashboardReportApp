namespace DashboardReportApp.Models
{
    using System;

    public class PressRunLogFormModel
    {
        // Login properties

        public long Id { get; set; }
        public DateTime? Timestamp { get; set; }
        public string Run { get; set; }
        public string Part { get; set; }
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string Operator { get; set; }
        public string Machine { get; set; }
        public int PcsStart { get; set; }
        public int PcsEnd { get; set; }
        public int Scrap { get; set; }
        public string Notes { get; set; }
    }



}
