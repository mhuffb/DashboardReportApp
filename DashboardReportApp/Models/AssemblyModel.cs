using System;

namespace DashboardReportApp.Models
{
    public class AssemblyModel
    {
        public long Id { get; set; }
        public DateTime? Timestamp { get; set; }
        public string? Operator { get; set; }
        public string? Part { get; set; }
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string? Notes { get; set; }
        public string ProdNumber { get; set; }
        public string? Run { get; set; }
        public sbyte? Open { get; set; }
        public int SkidNumber { get; set; }
        public int Pcs { get; set; }
    }
}

