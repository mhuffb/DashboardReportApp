using Microsoft.AspNetCore.Mvc;
using DashboardReportApp.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using DashboardReportApp.Models;

namespace DashboardReportApp.Controllers
{
    public class PressSetupController : Controller
    {
        private readonly PressSetupService _pressSetupService;

        public PressSetupController()
        {
            _pressSetupService = new PressSetupService();
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Press Setup";
            ViewData["Operators"] = GetOperators();
            ViewData["Machines"] = GetEquipment();
            ViewData["Trainers"] = GetTrainers();

            var pressSetupRecords = await _pressSetupService.GetAllPressSetupRecordsAsync();
            return View(pressSetupRecords);
        }

        private List<string> GetOperators()
        {
            // Fetch and return operators
            return new List<string> { "Operator A", "Operator B", "Operator C" };
        }

        private List<string> GetEquipment()
        {
            // Fetch and return equipment
            return new List<string> { "Machine 1", "Machine 2", "Machine 3" };
        }

        private List<string> GetTrainers()
        {
            // Fetch and return trainers
            return new List<string> { "Trainer A", "Trainer B" };
        }
    }
}
