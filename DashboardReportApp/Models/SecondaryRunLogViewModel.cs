namespace DashboardReportApp.Models
{
    public class SecondaryRunLogViewModel
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Run { get; set; }
        public string Part { get; set; }
        public string? Op { get; set; }
        public string Operator { get; set; }
        public DateTime StartDateTime { get; set; }
        public string Machine { get; set; }
        public string? Notes { get; set; }
        // Additional properties for dropdown lists
        public IEnumerable<string> Operators { get; set; }
        public IEnumerable<string> Machines { get; set; }
        public IEnumerable<SecondaryRunLogViewModel> OpenRuns { get; set; }
    }
}
