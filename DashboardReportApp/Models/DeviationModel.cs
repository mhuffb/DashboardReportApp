using System;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class DeviationModel
    {
        [Required(ErrorMessage = "Part is required.")]
        public string Part { get; set; }

        [Required(ErrorMessage = "Discrepancy is required.")]
        public string Discrepancy { get; set; }

        [Required(ErrorMessage = "Operator is required.")]
        public string Operator { get; set; }

        [Required(ErrorMessage = "Communication method is required.")]
        public string CommMethod { get; set; }

        // Nullable fields
        public string? Disposition { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? DateTimeCASTReview { get; set; }
    }
}
