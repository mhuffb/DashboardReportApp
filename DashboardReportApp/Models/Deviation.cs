namespace DashboardReportApp.Models
{
    public class Deviation
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Part { get; set; }
        public DateTime SentDateTime { get; set; }
        public string Discrepancy { get; set; }
        public string Operator { get; set; }
        public string CommMethod { get; set; }
        public string? Disposition { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? DateTimeCASTReview { get; set; }
    }

}
