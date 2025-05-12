namespace DashboardReportApp.Models
{
    public class FcEventDto
    {
        public string id { get; set; }
        public string title { get; set; }
        public string start { get; set; }   // ISO string
        public string end { get; set; }   // ISO string (or same date)
        public string color { get; set; }
        public bool allDay { get; set; }
        public decimal vacBalance { get; set; }
        public int reqDays { get; set; }

        public string explanation { get; set; } 
    }

}
