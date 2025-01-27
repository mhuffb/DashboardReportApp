using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class HoldRecordModel
    {
        public int Id { get; set; } // Primary Key

        public DateTime? Timestamp { get; set; } // Nullable DateTime

        public string? Part { get; set; } // Nullable string

        public string? Discrepancy { get; set; } // Nullable string

        public DateTime? Date { get; set; } // Nullable DateTime

        public string? IssuedBy { get; set; } // Nullable string

        public string? Disposition { get; set; } // Nullable string

        public string? DispositionBy { get; set; } // Nullable string

        public string? ReworkInstr { get; set; } // Nullable string

        public string? ReworkInstrBy { get; set; } // Nullable string

        public string? Quantity { get; set; } // Nullable string

        public string? Unit { get; set; } // Nullable string

        public int? PcsScrapped { get; set; } // Nullable integer

        public DateTime? DateCompleted { get; set; } // Nullable DateTime

        public string? FileAddress { get; set; } // Nullable string
    }


}
