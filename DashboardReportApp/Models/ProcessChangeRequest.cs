using System;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class ProcessChangeRequest
    {
        public int Id { get; set; }

        [DisplayFormat(DataFormatString = "{0:yyyy-MM-dd HH:mm:ss}")]
        public DateTime Timestamp { get; set; }

        [Required(ErrorMessage = "Part is required.")]
        public string? Part { get; set; }

        [Required(ErrorMessage = "Requester is required.")]
        public string? Requester { get; set; }

        public DateTime? ReqDate { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Request details are required.")]
        public string? Request { get; set; }

        public DateTime? UpdateDateTime { get; set; }
        public string? UpdatedBy { get; set; }
        public string? UpdateResult { get; set; }

        public string? FileAddress { get; set; }

        [Display(Name = "Media Link")]
        public string? FileAddressMediaLink { get; set; }

        public string? TestRequested { get; set; }
    }
}
