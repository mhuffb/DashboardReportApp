namespace DashboardReportApp.Models
{
    public class SecondaryRunLogModel
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public int Run { get; set; }
        public string Part { get; set; }
        public string Machine { get; set; }
        public string Operator { get; set; }
        public string? Op { get; set; }
        public int Pcs { get; set; }
        public int ScrapMach { get; set; }
        public int ScrapNonMach { get; set; }


        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        
        public string? Notes { get; set; }
        public string? Appearance { get; set; }
        // Additional properties for dropdown lists
        public IEnumerable<string> Operators { get; set; }
        public IEnumerable<string> Machines { get; set; }
        public IEnumerable<SecondaryRunLogModel> OpenRuns { get; set; }
    }
}
