namespace DashboardReportApp.Models
{
    public class MaintenanceRequest
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Equipment { get; set; }
        public string Requester { get; set; }
        public DateTime? RequestedDate { get; set; }
        public string Problem { get; set; }
        public DateTime? DownStartDateTime { get; set; }
        public DateTime? ClosedDateTime { get; set; }
        public string CloseBy { get; set; }
        public string CloseResult { get; set; }
        public bool DownStatus { get; set; }
        public int? HourMeter { get; set; }
        public bool HoldStatus { get; set; }
        public string HoldReason { get; set; }
        public string HoldResult { get; set; }
        public string HoldBy { get; set; }
        public string FileAddressImage { get; set; }
        public string FileAddressImageLink { get; set; }
        public string StatusHistory { get; set; }
        public string CurrentStatusBy { get; set; }
    }


}
