namespace DashboardReportApp.Models
{
    public class SinterRunSkid
    {
        public long Id { get; set; }
        public DateTime? Timestamp { get; set; }
        public string? Operator { get; set; }
        public string? Part { get; set; }
        public string? Component { get; set; }  // <-- New Property
        public string? Machine { get; set; }
        public string? Process { get; set; }
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string? Notes { get; set; }
        public string ProdNumber { get; set; }
        public string? Run { get; set; }
        public sbyte? Open { get; set; }
        public int SkidNumber { get; set; }
        public int Pcs { get; set; }
        public string LotNumber { get; set; }
        public string MaterialCode { get; set; }
        public string Source { get; set; }


    }

    public class SinterRunLogViewModel
    {
        public List<SinterRunSkid> PageItems { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }

        // Aux data
        public List<string> Operators { get; set; } = new();
        public List<string> Furnaces { get; set; } = new();
        public List<PressRunLogModel> OpenGreenSkids { get; set; } = new();
        public List<SinterRunSkid> OpenSinterRuns { get; set; } = new();
        public string? Search { get; set; }
        public string? Sort { get; set; } // column name
        public string? Dir { get; set; }  // "ASC" | "DESC"
        public HashSet<string> HoldKeys { get; set; }
      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string HoldKey(string source, string prod, string? run, string part, int skid)
        {
            return $"{source}|{prod?.Trim()}|{(run ?? "").Trim()}|{part?.Trim()}|{skid}";
        }

    }
}
