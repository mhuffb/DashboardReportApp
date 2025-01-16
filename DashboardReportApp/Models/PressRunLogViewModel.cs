namespace DashboardReportApp.Models
{
    using System;
    using System.Collections.Generic;

    public class PressRunLogViewModel
    {
        public List<string> OperatorList { get; set; }
        public List<string> EquipmentList { get; set; }
        public List<LoggedInRunModel> LoggedInRuns { get; set; }
        public PressRunLogFormModel FormModel { get; set; }
    }

    public class LoggedInRunModel
    {
        public string Operator { get; set; }
        public string Machine { get; set; }
        public string Part { get; set; }
        public DateTime StartDateTime { get; set; }
    }

}
