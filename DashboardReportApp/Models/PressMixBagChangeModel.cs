namespace DashboardReportApp.Models
{
    using System.ComponentModel.DataAnnotations;

    public class PressMixBagChangeModel
    {
        [Required]
        [Display(Name = "Part Number")]
        public string Part { get; set; }

        [Required]
        [Display(Name = "Operator")]
        public string Operator { get; set; }

        [Required]
        [Display(Name = "Machine")]
        public string Machine { get; set; }

        [Required]
        [Display(Name = "Lot Number")]
        public string LotNumber { get; set; }

        [Display(Name = "Notes")]
        public string Note { get; set; }

        [Required]
        [Display(Name = "Supplier Item Number")]
        public string SupplierItemNumber { get; set; }
        [Required]
        [Display(Name = "Mix Number")]
        public string MixNumber { get; set; } // New property
    }

}
