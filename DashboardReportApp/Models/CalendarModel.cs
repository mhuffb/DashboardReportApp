namespace DashboardReportApp.Models
{
    public class CalendarModel
    {
        public int Id { get; set; }   // NEW
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime DateEmployed { get; set; }
        public string ActiveStatus { get; set; }
        public string Email { get; set; }
       
        public decimal VacationBalance { get; set; }

        public string Department { get; set; }
        public string Shift { get; set; }
        public string Schedule { get; set; }
        public string Attribute { get; set; }
        public string Explanation { get; set; }
        public string TimeOffType { get; set; }
        public List<DateTime> DatesRequested { get; set; }

        public string Status { get; set; }   // NEW  ("Waiting" / "Approved")
        public DateTime SubmittedOn { get; set; }

    }


}
