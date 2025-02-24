using System;

namespace DashboardReportApp.Models
{
    public class AdminDeviationModel
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Part { get; set; }
        public DateTime? SentDateTime { get; set; }
        public string Discrepancy { get; set; }
        public string Operator { get; set; }
        public string CommMethod { get; set; }
        public string Disposition { get; set; }
        public string ApprovedBy { get; set; }
        public DateTime? DateTimeCASTReview { get; set; }
        public string? FileAddress1 { get; set; } // Nullable string
        public string? FileAddress2 { get; set; } // Nullable string
    }
}

