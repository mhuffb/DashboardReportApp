// Models/ToolItemModel.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public enum ToolCategory { TopPunch = 1, BottomPunch = 2, Die = 3, Pin = 4 }
    public enum ToolCondition { ReadyForProduction = 1, Questionable = 2, NeedsWork = 3, DoNotUse = 4 }
    public enum ToolStatus { Available = 1, Unavailable = 2 }

    public class ToolItemModel
    {
        public int Id { get; set; }
        [Required, StringLength(64)] public string AssemblyNumber { get; set; } = string.Empty;
        [Required, StringLength(64)] public string ToolNumber { get; set; } = string.Empty;
        [StringLength(128)] public string? Location { get; set; }
        [Required] public ToolCategory Category { get; set; }
        [Required] public ToolCondition Condition { get; set; }
        [Required] public ToolStatus Status { get; set; }
        [StringLength(255)] public string? UnavailableReason { get; set; }
        [DataType(DataType.Date)] public DateTime? DateUnavailable { get; set; }
        [DataType(DataType.Date)] public DateTime? EstimatedAvailableDate { get; set; }
        public string CategoryLabel => Category switch
        {
            ToolCategory.TopPunch => "Top Punch",
            ToolCategory.BottomPunch => "Bottom Punch",
            ToolCategory.Die => "Die",
            ToolCategory.Pin => "Pin",
            _ => Category.ToString()
        };
    }
}
