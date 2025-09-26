using System;
using System.Collections.Generic;

namespace DashboardReportApp.Models
{
    public class ToolingHistoryModel
    {
        public int Id { get; set; }
        public int GroupID { get; set; }
        public string Part { get; set; }
        public string ToolNumber { get; set; }
        public string Revision { get; set; }
        public string PO { get; set; }
        public string Reason { get; set; }
        public string ToolVendor { get; set; }
        public DateTime DateInitiated { get; set; }
        public DateTime? DateDue { get; set; }           // nullable so Razor can use ?.
        public decimal? Cost { get; set; }
        public int? ToolWorkHours { get; set; }
        public string ToolDesc { get; set; }
        public int? AccountingCode { get; set; }

        // NEW:
        public string? InitiatedBy { get; set; }          // picker (default: Emery, J)
        public DateTime? DateReceived { get; set; }       // header received date
    }

    public class GroupToolItem
    {
        public int Id { get; set; }
        public int GroupID { get; set; }

        public string Action { get; set; }       // Make New / Metalife Coat / ...
        public string ToolItem { get; set; }     // Top Punch / Bottom Punch / ...
        public string ToolNumber { get; set; }
        public string ToolDesc { get; set; }
        public string Revision { get; set; }

        public int? Quantity { get; set; }
        public decimal? Cost { get; set; }
        public decimal? ToolWorkHours { get; set; }

        public DateTime? DateDue { get; set; }
        public DateTime? DateFitted { get; set; }
        public DateTime? DateReceived { get; set; }

        public string ReceivedBy { get; set; }   // from recvFit list
        public string FittedBy { get; set; }     // from recvFit list
    }

    // View model used by forms/modals (matches what the Controller expects)
    public sealed class ToolItemViewModel
    {
        public int? Id { get; set; }
        public int GroupID { get; set; }                 // NOTE: GroupID (not GroupId) to match your controller

        public string? Action { get; set; }
        public string? ToolItem { get; set; }
        public string? ToolNumber { get; set; }
        public string? ToolDesc { get; set; }
        public string? Revision { get; set; }

        public int? Quantity { get; set; }
        public decimal? Cost { get; set; }
        public decimal? ToolWorkHours { get; set; }

        public DateTime? DateDue { get; set; }
        public DateTime? DateFitted { get; set; }
        public DateTime? DateReceived { get; set; }

        public string? ReceivedBy { get; set; }
        public string? FittedBy { get; set; }

        // Optional audit-ish
        public string? InitiatedBy { get; set; }
        public string? Reason { get; set; }
    }

    // Matches how your controller currently uses it (GroupID, ToolItems, NewToolItem)
    public sealed class GroupDetailsViewModel
    {
        public int GroupID { get; set; }
        public string? GroupName { get; set; }

        // MUST be ToolItemViewModel to match _service.GetToolItemsByGroupID(...)
        public List<ToolItemViewModel> ToolItems { get; set; } = new();

        // Form-backing object for "Add New"
        public ToolItemViewModel NewToolItem { get; set; } = new();
    }

}
