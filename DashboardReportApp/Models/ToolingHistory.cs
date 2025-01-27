using System;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class ToolingHistory
    {
        public int Id { get; set; }
        public int GroupID { get; set; } // New property for GroupID
        public string Reason { get; set; }
        public string ToolVendor { get; set; }
        public DateTime? DateInitiated { get; set; }
        public DateTime? DateDue { get; set; }
        public string Part { get; set; }
        public string ToolNumber { get; set; }
        public decimal? Cost { get; set; } // Nullable
        public string? Revision { get; set; }
        public string? PO { get; set; }
        public int? ToolWorkHours { get; set; }
        public string? ToolDesc { get; set; }
        public int? AccountingCode { get; set; }

    }
    public class ToolItemViewModel
    {
        [Required(ErrorMessage = "Group ID is required.")]
        public int GroupID { get; set; }

        [Required(ErrorMessage = "Tool Number is required.")]
        public string ToolNumber { get; set; }

        [Required(ErrorMessage = "Tool Description is required.")]
        public string ToolDesc { get; set; }
        [Required(ErrorMessage = "Tool Item is required.")]
        public string ToolItem { get; set; }
        public decimal? Cost { get; set; }
        public string? Revision { get; set; }
        public int? Quantity { get; set; }
        public int? ToolWorkHours { get; set; }
        public DateTime? DateDue { get; set; }
        public DateTime? DateFitted { get; set; }
        public string? ReceivedBy { get; set; }
        public string? FittedBy { get; set; }
        public string? Action { get; set; }
    }

    public class GroupDetailsViewModel
    {
        public int GroupID { get; set; }
        public List<ToolItemViewModel> ToolItems { get; set; }
        public ToolItemViewModel NewToolItem { get; set; }
    }

}
