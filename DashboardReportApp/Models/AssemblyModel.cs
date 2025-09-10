using System;

namespace DashboardReportApp.Models
{
    public class AssemblyModel
    {
        public int Id { get; set; }
        public string? Operator { get; set; }
        public string? Part { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string? Notes { get; set; }
        public string? ProdNumber { get; set; }
        public sbyte? Open { get; set; }
        public int? SkidNumber { get; set; }
        public int? Pcs { get; set; }
    }

    public class AssemblyRunViewModel
    {
        public List<AssemblyModel> PageItems { get; set; } = new();
        public List<PressRunLogModel> OpenGreenSkids { get; set; } = new();
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int Total { get; set; } = 0;
        public string Search { get; set; } = "";
        public string Sort { get; set; } = "id";
        public string Dir { get; set; } = "DESC";
    }
}

