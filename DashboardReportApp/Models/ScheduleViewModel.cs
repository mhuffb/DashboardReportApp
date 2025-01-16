namespace DashboardReportApp.Models
{
    public class ScheduleViewModel
    {
        public List<SintergyComponent> AllComponents { get; set; } // For the query results
        public List<SintergyComponent> OpenParts { get; set; } // Existing Open Parts data
    }


    public class SintergyComponent
    {
        public string MasterId { get; set; } // Maps to 'part'
        public string Component { get; set; }
        public int QtyToMakeMasterID { get; set; }
        public int QtyToSchedule { get; set; }
        public string Run { get; set; } // Next run number
        public int? SinterGroup { get; set; }  // Next sintergroup number or null
        public DateTime? Date { get; set; } // Nullable DateTime for 'date' column
    }



}
