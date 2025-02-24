using System;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class DeviationModel
    {
        public int Id { get; set; } // Primary Key

        [Required(ErrorMessage = "Part is required.")]
        public string Part { get; set; }

        [Required(ErrorMessage = "Discrepancy is required.")]
        public string Discrepancy { get; set; }

        public DateTime? SentDateTime { get; set; }

        [Required(ErrorMessage = "Operator is required.")]
        public string Operator { get; set; }

        [Required(ErrorMessage = "Communication method is required.")]
        public string CommMethod { get; set; }

        // Nullable fields
        public string? Disposition { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? DateTimeCASTReview { get; set; }
        public string? FileAddress1 { get; set; } // Nullable string
        public string? FileAddress2 { get; set; } // Nullable string
    }
    public class DeviationIndexViewModel
    {
        public DeviationModel FormModel { get; set; }
        public List<DeviationModel> Records { get; set; }
    }
}

