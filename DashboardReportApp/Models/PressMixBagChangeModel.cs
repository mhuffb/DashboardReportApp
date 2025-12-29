namespace DashboardReportApp.Models
{
    using System.ComponentModel.DataAnnotations;

    public class PressMixBagChangeModel
    {
        public int Id { get; set; }
        public string Part { get; set; }
        public string? Component { get; set; }
        public string ProdNumber { get; set; }
        public string Run { get; set; }
        public string Operator { get; set; }
        public string Machine { get; set; }
        public string LotNumber { get; set; }
        public string MaterialCode { get; set; }
        public decimal? WeightLbs { get; set; }
        public string? BagNumber { get; set; }
        public DateTime? SentDateTime { get; set; }
        public string? Notes { get; set; }
        public bool? IsOverride { get; set; }
        public string? OverrideBy { get; set; }
        public DateTime? OverrideAt { get; set; }
        public string? OverrideReason { get; set; }

    }


}
