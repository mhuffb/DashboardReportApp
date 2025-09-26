using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class ToolingHistoryModel
    {
        public int Id { get; set; }

        // Not required at POST time; service ensures GroupID after insert.
        public int GroupID { get; set; }

        [Required(ErrorMessage = "Assembly # (Part) is required.")]
        public string Part { get; set; }

        // OPTIONAL now
        public string ToolNumber { get; set; }

        // OPTIONAL now
        public string Revision { get; set; }

        // OPTIONAL now
        public string PO { get; set; }

        [Required(ErrorMessage = "Reason is required.")]
        public string Reason { get; set; }

        [Required(ErrorMessage = "Tool vendor is required.")]
        public string ToolVendor { get; set; }

        [Required(ErrorMessage = "Initiated By is required.")]
        public string InitiatedBy { get; set; }

        [DataType(DataType.Date)]
        public DateTime? DateInitiated { get; set; }

        // OPTIONAL now
        [DataType(DataType.Date)]
        public DateTime? DateDue { get; set; }

        public decimal? Cost { get; set; }
        public decimal? ToolWorkHours { get; set; }

        // OPTIONAL now
        public string ToolDesc { get; set; }
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
        public DateTime? DateReceived { get; set; }   // stays on Tool Items

        public string ReceivedBy { get; set; }
        public string FittedBy { get; set; }
    }

    public class GroupDetailsViewModel
    {
        public int GroupID { get; set; }
        public List<GroupToolItem> ToolItems { get; set; } = new();
    }
}
