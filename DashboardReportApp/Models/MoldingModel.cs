using System;
using System.Collections.Generic;

namespace DashboardReportApp.Models
{
    public class MoldingModel
    {
        public List<PressRunLogModel> PressRuns { get; set; }
        public List<PressSetupModel> PressSetups { get; set; }
        public List<PressMixBagChangeModel> PressLotChanges { get; set; }

        public string SearchTerm { get; set; }
        public string SortColumn { get; set; }
        public bool SortDescending { get; set; }
    }
}
