using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class SecondarySetupLogViewModel
    {
        // Properties for adding a new setup
        [Required]
        public string Operator { get; set; }

        [Required]
        public string Machine { get; set; }

        [Required]
        public string Run { get; set; }

        [Required]
        public string Op { get; set; } // Added Op field
        

        public int? Pcs { get; set; }
        public int? ScrapMach { get; set; }
        public int? ScrapNonMach { get; set; }
        public string Notes { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Setup hours must be a positive number.")]
        public decimal SetupHours { get; set; }

        // Properties for populating dropdowns
        public List<string> Operators { get; set; } = new List<string>();
        public List<string> Equipment { get; set; } = new List<string>();

    }

}
