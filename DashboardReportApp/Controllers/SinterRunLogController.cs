using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    [Route("SinterRunLog")]
    public class SinterRunLogController : Controller
    {
        private readonly SinterRunLogService _sinterRunLogService;

        public SinterRunLogController(SinterRunLogService sinterRunLogService)
        {
            _sinterRunLogService = sinterRunLogService;
        }

        public async Task<IActionResult> Index()
        {
            var operators = _sinterRunLogService.GetOperators();
            // Ensure it's not null, if it is, assign an empty list
            ViewData["Operators"] = operators ?? new List<string>();

            var furnaces = _sinterRunLogService.GetFurnaces();
            ViewData["Furnaces"] = furnaces ?? new List<string>();

            var openSkids = _sinterRunLogService.GetOpenSkids();
            ViewData["OpenSkids"] = openSkids ?? new List<SinterRunSkid>();

            // 2) Fetch open parts and runs from `pressrunlog` where `open = 1`
            var openRuns = await _sinterRunLogService.GetOpenRunsAsync();
            if (openRuns == null || !openRuns.Any())
            {
                Console.WriteLine("No open runs found! Check the database query.");
            }

            // Create a dictionary of (Part, Run) => Furnace for dropdown selection
            var openParts = openRuns.ToDictionary(
                r => (r.Part, r.Run), // Key: (Part, Run)
                r => r.Machine         // Value: Furnace/Machine
            );
            Console.WriteLine($"OpenParts Count: {openParts.Count}"); // Check if dictionary is populated


            ViewData["OpenParts"] = openParts;
            Console.WriteLine($"✅ OpenParts Dictionary Count: {openParts.Count}");
            foreach (var partRun in openParts)
            {
                Console.WriteLine($"Part: {partRun.Key.Item1}, Run: {partRun.Key.Item2}, Furnace: {partRun.Value}");
            }

            // 3) Fetch open runs (`open = 1`) for the table display
            ViewBag.OpenRuns = openRuns;


            // "All Runs" = entire sinterrun table
            var allRuns = await _sinterRunLogService.GetAllRunsAsync();

            // Return all runs as the model (for React table)
            return View(allRuns);
        }

        [HttpPost("StartSkid")]
        public IActionResult StartSkid(string operatorName, string part, string run, string furnace, string process, string notes)
        {
            Console.WriteLine("📩 Received Data:");
            Console.WriteLine($"➡️ Operator: {operatorName}");
            Console.WriteLine($"➡️ Part: {part}");
            Console.WriteLine($"➡️ Run: {run}");  // <-- Check if this is empty
            Console.WriteLine($"➡️ Furnace: {furnace}");
            Console.WriteLine($"➡️ Process: {process}");
            Console.WriteLine($"➡️ Notes: {notes}");
            if (string.IsNullOrWhiteSpace(operatorName) || string.IsNullOrWhiteSpace(part) || string.IsNullOrWhiteSpace(run) ||
                string.IsNullOrWhiteSpace(furnace) || string.IsNullOrWhiteSpace(process))
            {
                ViewData["Error"] = "All fields are required.";
                return RedirectToAction("Index");
            }

            try
            {
                // Check if a skid is already running on the selected furnace
                _sinterRunLogService.EndSkidsByFurnaceIfNeeded(furnace);

                // Start the new skid with separate Part and Run
                _sinterRunLogService.StartSkid(operatorName, part, run, furnace, process, notes);
                ViewData["Message"] = $"{part} - {run} started successfully on Furnace {furnace}.";
            }
            catch (Exception ex)
            {
                ViewData["Error"] = $"An error occurred: {ex.Message}";
            }

            return RedirectToAction("Index");
        }


        [HttpPost("CloseSkid")]
        public IActionResult CloseSkid(string part, string run)
        {
            try
            {
                Console.WriteLine($"🚨 Closing Sinter Run: Part = {part}, Run = {run}");

                _sinterRunLogService.CloseSkid(part, run);
                ViewData["Message"] = $"Sintering stopped for {part} - {run} successfully.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error Closing Sinter Run: {ex.Message}");
                ViewData["Error"] = $"An error occurred: {ex.Message}";
            }

            return RedirectToAction("Index");
        }


        [HttpPost("CloseSkidByFurnace")]
        public IActionResult CloseSkidByFurnace(string furnace)
        {
            try
            {
                _sinterRunLogService.CloseSkidsByFurnace(furnace);
                ViewData["Message"] = $"All open skids on Furnace {furnace} closed successfully.";
            }
            catch (Exception ex)
            {
                ViewData["Error"] = $"An error occurred: {ex.Message}";
            }

            return RedirectToAction("Index");
        }
    }
}
