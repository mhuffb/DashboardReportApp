using System;

namespace DashboardReportApp.Models
{
    public class PressSetupModel
    {
        public long Id { get; set; }
        public DateTime? Timestamp { get; set; }
        public string Part { get; set; }
        public string Run { get; set; }
        public string Operator { get; set; }
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string Machine { get; set; }
        public string PressType { get; set; }
        public string Difficulty { get; set; }
        public string SetupComp { get; set; }
        public string AssistanceReq { get; set; }
        public string AssistedBy { get; set; }
        public string Notes { get; set; }
    }
}
