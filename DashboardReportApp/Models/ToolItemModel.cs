// Models/ToolItemModel.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public enum ToolCategory { TopPunch = 1, BottomPunch = 2, Die = 3, Pin = 4 }
    public enum ToolCondition { ReadyForProduction = 1, Questionable = 2, Obsolete = 3 }
    public enum ToolStatus { Available = 1, Unavailable = 2 }

    public class ToolItemModel
    {
        public int Id { get; set; }
        [Required, StringLength(64)] public string AssemblyNumber { get; set; } = string.Empty;
        [Required, StringLength(64)] public string ToolNumber { get; set; } = string.Empty;
        [StringLength(128)] public string? Location { get; set; }

        public string ToolItem { get; set; } = ""; // ⬅ replaces Category
        public ToolCondition? Condition { get; set; }     // now nullable
        public ToolStatus? Status { get; set; }           // now nullable

        public string? UnavailableReason { get; set; }
        public DateTime? DateUnavailable { get; set; }
        public DateTime? EstimatedAvailableDate { get; set; }
       
    }
}
