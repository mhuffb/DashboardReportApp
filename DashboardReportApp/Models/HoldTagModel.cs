using System.ComponentModel.DataAnnotations;

namespace DashboardReportApp.Models
{
    public class HoldTagModel
    {
        public int Id { get; set; } // Primary Key

        public DateTime? Timestamp { get; set; } // Nullable DateTime

        public string? Part { get; set; } // Nullable string
        public string? Component { get; set; } // Nullable string

        public string? Discrepancy { get; set; } // Nullable string

        public DateTime? Date { get; set; } // Nullable DateTime

        [Required(ErrorMessage = "Issued By is required.")]
        public string? IssuedBy { get; set; }

        public string? Disposition { get; set; } // Nullable string

        public string? DispositionBy { get; set; } // Nullable string

        public string? ReworkInstr { get; set; } // Nullable string

        public string? ReworkInstrBy { get; set; } // Nullable string


        public string? Unit { get; set; } // Nullable string

        public int? PcsScrapped { get; set; } // Nullable integer

        public DateTime? DateCompleted { get; set; } // Nullable DateTime

        public string? FileAddress1 { get; set; } // Nullable string
        public string? FileAddress2 { get; set; } // Nullable string
        [Required(ErrorMessage = "Production number is required.")]
        public string? ProdNumber { get; set; }
        public string? LotNumber { get; set; }
        public string? MaterialCode { get; set; }

        // existing:
        public int? Quantity { get; set; }      // quantity of hold tags
                                                // NEW:
        public int? QuantityOnHold { get; set; }  // total pcs/skids/etc on hold

        public string? RunNumber { get; set; }
        public string? Source { get; set; }
        public int? SkidNumber { get; set; }
        public string? Stage { get; set; }

    }
    public class HoldTagIndexViewModel
    {
        public HoldTagModel FormModel { get; set; }
        public List<HoldTagModel> Records { get; set; }

    }
    public static class HoldKeyHelper
    {
        public static string HoldKey(string source, string prod, string? run, string part, int skid)
        {
            return $"{source}|{prod?.Trim()}|{(run ?? "").Trim()}|{part?.Trim()}|{skid}";
        }
    }
}
