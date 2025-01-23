namespace DashboardReportApp.Models
{
    public class HoldRecord
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Part { get; set; }
        public string Discrepancy { get; set; }
        public DateTime Date { get; set; }
        public string IssuedBy { get; set; }
        public string Disposition { get; set; }
        public string DispositionBy { get; set; }
        public string ReworkInstr { get; set; }
        public string ReworkInstrBy { get; set; }
        public string Quantity { get; set; }
        public string Unit { get; set; }
        public int? PcsScrapped { get; set; }
        public DateTime? DateCompleted { get; set; }
        public string FileAddress { get; set; }
    }
}
