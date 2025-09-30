namespace DashboardReportApp.Models
{
    public class CalendarEventModel
    {
        internal string DateString;

        public int Id { get; set; }
        public string Title { get; set; }
        public string Location { get; set; }
        public string Description { get; set; }
        public DateTime Date { get; set; }
        public DateTime SubmittedOn { get; set; }

        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Scheduler { get; set; }
        public string SeriesId { get; set; }          // null for one-offs
        public bool IsSeed { get; set; }              // true on the first created row
        public string RecurRule { get; set; } = "None";   // None | Daily | Weekly | Monthly
        public DateTime? RecurUntil { get; set; }
    }

}
