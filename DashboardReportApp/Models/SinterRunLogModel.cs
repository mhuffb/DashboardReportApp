﻿namespace DashboardReportApp.Models
{
    public class SinterRunSkid
    {
        public long Id { get; set; }
        public DateTime? Timestamp { get; set; }
        public string? Operator { get; set; }
        public string? Part { get; set; }
        public string? Machine { get; set; }
        public string? Process { get; set; }
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string? Notes { get; set; }
        public string? Run { get; set; }
        public sbyte? Open { get; set; }
    }

}
