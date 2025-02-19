namespace DashboardReportApp.Models
{
    using System;
    using System.Collections.Generic;

    public class PressRunLogModel
    {
        public long Id { get; set; }
        public DateTime? Timestamp { get; set; }
        public string ProdNumber { get; set; }
        public string Run { get; set; }
        public string Part { get; set; }
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string Operator { get; set; }
        public string Machine { get; set; }
        public int PcsStart { get; set; }
        public int PcsEnd { get; set; }
        public int Scrap { get; set; }
        public string? Notes { get; set; }
        public sbyte Open { get; set; }
        public int SkidCount { get; set; }
    }

    public class OpenSetups
    {

    }
   
}
