namespace DashboardReportApp.Models
{
    public class ScheduleViewModel
    {
        public List<SintergyComponent> AllComponents { get; set; } // For the query results
        public List<SintergyComponent> OpenParts { get; set; } // Existing Open Parts data
    }


    public class SintergyComponent
    {
        public DateTime? Date { get; set; }
        public string MasterId { get; set; }
        public string Component { get; set; }
        public string SubComponent { get; set; }
        public int QtyToMakeMasterID { get; set; }
        public int QtyToSchedule { get; set; }
        public string Run { get; set; }
        public int Open { get; set; } // Add this property
    }



}
