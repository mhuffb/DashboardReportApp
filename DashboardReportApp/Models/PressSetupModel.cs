using System;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class PressSetupModel
    {
        public long Id { get; set; }
        public DateTime? Timestamp { get; set; }
        public string Part { get; set; }
        public string Run { get; set; }
        public string Operator { get; set; }
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string Machine { get; set; }
        public string PressType { get; set; }
        public string Difficulty { get; set; }
        public string SetupComp { get; set; }
        public string AssistanceReq { get; set; }
        public string AssistedBy { get; set; }
        public string Notes { get; set; }
        public sbyte Open { get; set; }
        public string ProdNumber { get; set; }
        public string? Component { get; set; }
        public string Subcomponent { get; set; }
        public decimal ElapsedTime { get; set; }

        public string MaterialCode { get; set; }
        public string LotNumber { get; set; }
    }
    public class PressSetupLoginViewModel
    {
        // Only include fields that are submitted from the form.
        [Required]
        public string Part { get; set; }

        [Required]
        public string Run { get; set; }

        [Required]
        public string Operator { get; set; }

        [Required]
        public string Machine { get; set; }

        // If prodNumber is required:
        public string ProdNumber { get; set; }
        public string Component { get; set; }
        public string Subcomponent { get; set; }
    }

    public class Scheduled
    {
        public long Id { get; set; }
        public string Part { get; set; }
        public string Component { get; set; }
        public string Subcomponent { get; set; }
        public string ProdNumber { get; set; }
        public string Run { get; set; }
    }
    }
