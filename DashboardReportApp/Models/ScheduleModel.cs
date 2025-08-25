namespace DashboardReportApp.Models
{
    public class ScheduleModel
    {
        public List<SintergyComponent> AllComponents { get; set; } // For the query results
        public List<SintergyComponent> AllParts { get; set; } // Existing Open Parts data
    }


    public class SintergyComponent
    {
        public int Id { get; set; }
        public DateTime? Date { get; set; }
        public string MasterId { get; set; }
        public string Part { get; set; }
        public string Component { get; set; }
        public int? QtyNeededFor1Assy { get; set; }
        public int QtyToSchedule { get; set; }
        public int AssyGPcs { get; set; }
        public string Run { get; set; }
        public int Open { get; set; } // Add this property
        public string ProdNumber { get; set; }
        public sbyte GetsSintergySecondary { get; set; }
        public string MaterialCode { get; set; }

    }
    public class ScheduleItem
    {
        public string Part { get; set; }
        public string ProdNumber { get; set; }
        public int? Run { get; set; } // Make sure this is a property, not a method
        public int NumberOfSintergySecondaryOps { get; set; }
    }


    public class PowderMixEntry
    {
        public int Id { get; set; }           
        public int LotNumber { get; set; }
        public decimal WeightLbs { get; set; }
        public string MaterialCode { get; set; }
        public DateTime CreatedAt { get; set; }
    }


}
