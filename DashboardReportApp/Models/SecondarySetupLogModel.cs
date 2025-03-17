using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class SecondarySetupLogModel
    {
        // Properties for adding a new setup
        [Required]
        public string? Operator { get; set; }

        [Required]
        public string? Machine { get; set; }
        public string? ProdNumber { get; set; }
        [Required]
        public int? Run { get; set; }

        public string? Op { get; set; } // Added Op field
        public string? Part { get; set; }
        public int? Id { get; set; }
        public int? Pcs { get; set; }
        public int? ScrapMach { get; set; }
        public int? ScrapNonMach { get; set; }
        public string? Notes { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Setup hours must be a positive number.")]
        public decimal? SetupHours { get; set; }

        // Properties for populating dropdowns
        public List<string> Operators { get; set; } = new List<string>();
        public List<string> Equipment { get; set; } = new List<string>();
        public DateTime? Timestamp { get; set; }
        public sbyte? Open { get; set; }
    }

}
