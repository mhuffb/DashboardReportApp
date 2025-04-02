namespace DashboardReportApp.Models
{
    using System.ComponentModel.DataAnnotations;

    public class PressMixBagChangeModel
    {
        public long Id { get; set; }
        public string Part { get; set; }
        public string Component { get; set; }
        public string ProdNumber { get; set; }
        public string Run { get; set; }
        public DateTime? SentDateTime { get; set; }
        public string Operator { get; set; }
        public string Machine { get; set; }
        public string LotNumber { get; set; }
        public string MixNumber { get; set; }
        public string? Notes { get; set; }
        public string? SupplierItemNumber { get; set; }

    }

}
