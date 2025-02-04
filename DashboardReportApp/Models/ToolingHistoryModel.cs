using System;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class ToolingHistoryModel
    {
        public int Id { get; set; }
        public int GroupID { get; set; } // New property for GroupID
        [Required(ErrorMessage = "Reason is required.")]
        public string Reason { get; set; } = default!;
        [Required(ErrorMessage = "Please select a Tool Vendor.")]
        public string ToolVendor { get; set; } = default!;

        public DateTime? DateInitiated { get; set; }
        public DateTime? DateDue { get; set; }

        [Required(ErrorMessage = "Assembly # is required.")]
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
        public int Id { get; set; }      // Unique identifier from DB
        [Required(ErrorMessage = "Group ID is required.")]
        public int GroupID { get; set; }

        public string? ToolNumber { get; set; }

        public string? ToolDesc { get; set; }
        public string ToolItem { get; set; }
        public decimal? Cost { get; set; }
        public string? Revision { get; set; }
        public int? Quantity { get; set; }
        public int? ToolWorkHours { get; set; }
        public DateTime? DateDue { get; set; }
        public DateTime? DateFitted { get; set; }
        public string? ReceivedBy { get; set; }
        public string? FittedBy { get; set; }
        public string Action { get; set; }
    }

    public class GroupDetailsViewModel
    {
        public int GroupID { get; set; }
        public List<ToolItemViewModel> ToolItems { get; set; }
        public ToolItemViewModel NewToolItem { get; set; }
    }

}
