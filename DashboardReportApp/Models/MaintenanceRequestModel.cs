using System;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class MaintenanceRequestModel
    {
        public int Id { get; set; }

        public DateTime? Timestamp { get; set; }

        [Required(ErrorMessage = "The Equipment field is required.")]
        public string? Equipment { get; set; }

        [Required(ErrorMessage = "The Requester field is required.")]
        public string? Requester { get; set; }

        public DateTime? RequestedDate { get; set; }
        public string? Problem { get; set; }
        public DateTime? DownStartDateTime { get; set; }
        public DateTime? ClosedDateTime { get; set; }
        public string? CloseBy { get; set; }
        public string? CloseResult { get; set; }

        public bool? DownStatus { get; set; }

        public decimal? HourMeter { get; set; }

        public bool? HoldStatus { get; set; }
        public string? HoldReason { get; set; }
        public string? HoldResult { get; set; }
        public string? HoldBy { get; set; }
        public string? FileAddress { get; set; }
        public string? MaintenanceRequestFile1 { get; set; }
        public string? MaintenanceRequestFile2 { get; set; }
        public string? StatusHistory { get; set; }
        public string? CurrentStatusBy { get; set; }
        public string? Department { get; set; }
        public string? Status { get; set; }
        public string? StatusDesc { get; set; }
        public string? NewStatusDesc { get; set; }
        public string? StatusUpdatedBy { get; set; }

    }
   

}
