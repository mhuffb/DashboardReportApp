using System;
using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class ProcessChangeRequestModel
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


        public string? FileAddress1 { get; set; }

        public string? FileAddress2 { get; set; }

        public string? TestRequested { get; set; }
    }
}
