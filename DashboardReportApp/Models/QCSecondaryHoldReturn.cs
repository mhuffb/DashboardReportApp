namespace DashboardReportApp.Models
{
    public class QCSecondaryHoldReturn
    {
        public int Id { get; set; }
        public string Operator { get; set; }
        public string Run { get; set; }
        public int QtyReturnedMachined { get; set; }
        public int QtyReturnedNonMachined { get; set; }
        public string Notes { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
