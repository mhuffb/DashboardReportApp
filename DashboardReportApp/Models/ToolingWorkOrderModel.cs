using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class ToolingWorkOrderModel
    {
        public int Id { get; set; }
        [Required]
        public string Part { get; set; }
        public string? PO { get; set; }
        [Required]
        public string Reason { get; set; }
        [Required]
        public string ToolVendor { get; set; }
        public DateTime DateInitiated { get; set; }
        public DateTime? DateDue { get; set; }           // nullable so Razor can use ?.
        public decimal? Cost { get; set; }
        public string? ToolDesc { get; set; }
        public int? AccountingCode { get; set; }

        [Required]
        public string InitiatedBy { get; set; }          // picker (default: Emery, J)
        public DateTime? DateReceived { get; set; }       // header received date
        public DateTime? PoRequestedAt { get; set; }
        public string? Received_CompletedBy { get; set; }
        public string? AttachmentFileName { get; set; }
        public DateTime? PackingSlipCreatedAt { get; set; }
        public DateTime? DateSent { get; set; }          // when tools were sent out

    }



    // View model used by forms/modals (matches what the Controller expects)
    public sealed class ToolItemViewModel
    {
        public int Id { get; set; }
        [Required]
        public int HeaderId { get; set; }                
        [Required]
        public string Action { get; set; }
        [Required]
        public string ToolItem { get; set; }
        [Required]
        public string ToolNumber { get; set; }
        public string? ToolDesc { get; set; }
        public string? Revision { get; set; }
        [Display(Name = "Qty")]
        [Required(ErrorMessage = "{0} is required.")]
        [Range(0, int.MaxValue, ErrorMessage = "{0} must be a whole number ≥ 0.")]
        public int? Quantity { get; set; }   // nullable + Required -> empty becomes a clean “Qty is required.”

        public decimal? Cost { get; set; }
        public decimal? ToolWorkHours { get; set; }

       

       

        // Optional audit-ish
        public string? InitiatedBy { get; set; }
        public string? Reason { get; set; }
    }

    // Matches how your controller currently uses it (GroupID, ToolItems, NewToolItem)
    public sealed class GroupDetailsViewModel
    {
        public int HeaderId { get; set; }
        public string? GroupName { get; set; }

        // MUST be ToolItemViewModel to match _service.GetToolItemsByGroupID(...)
        public List<ToolItemViewModel> ToolItems { get; set; } = new();

        // Form-backing object for "Add New"
        public ToolItemViewModel NewToolItem { get; set; } = new();
    }
    public class CompleteWorkOrderVM
    {
        [Required]
        public int Id { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime DateReceived { get; set; }

        [Required(ErrorMessage = "Received/Completed By is required.")]
        public string Received_CompletedBy { get; set; } = string.Empty;
    }
    public class ToolingAttachmentPreviewVM
    {
        public int Id { get; set; }
        public string? FileUrl { get; set; }
        public string? FileName { get; set; }
    }

}
