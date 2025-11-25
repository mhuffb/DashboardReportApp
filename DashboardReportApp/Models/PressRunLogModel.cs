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
        public string Component { get; set; }
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string Operator { get; set; }
        public string Machine { get; set; }
        public int? PcsStart { get; set; }
        public int? PcsEnd { get; set; }
        public int? Scrap { get; set; }
        public string? Notes { get; set; }
        public sbyte Open { get; set; }
        public int SkidNumber { get; set; }
        public int Pcs { get; set; }
        public decimal ElapsedTime { get; set; }
        public decimal CycleTime { get; set; }
        public string LotNumber { get; set; }
        public string MaterialCode { get; set; }
        public bool IsOverride { get; set; }
        public string? OverrideBy { get; set; }
        public DateTime? OverrideAt { get; set; }


    }

    public class OpenSetups
    {

    }
   
}
