using DashboardReportApp.Models;
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
            // 1) Get operators and furnaces from the service
            var operators = _sinterRunLogService.GetOperators();
            ViewData["Operators"] = operators ?? new List<string>();

            var furnaces = _sinterRunLogService.GetFurnaces();
            ViewData["Furnaces"] = furnaces ?? new List<string>();

            // 2) Fetch open skids (where open = 1 and skidcount > 0) from the pressrun table
            var openGreenSkids = await _sinterRunLogService.GetOpenGreenSkidsAsync();
            ViewBag.OpenGreenSkids = openGreenSkids ?? new List<PressRunLogModel>();

            // 3) Create a dictionary of (Part, Run) => Furnace for dropdown selection from open skids
           var openParts = openGreenSkids.ToDictionary(
    r => (r.Part, r.Run, r.SkidCount),
    r => r.Machine
);

            ViewData["OpenParts"] = openParts;
            foreach (var partRun in openParts)
            {
                Console.WriteLine($"Part: {partRun.Key.Item1}, Run: {partRun.Key.Item2}, Furnace: {partRun.Value}");
            }

            // 4) Get all sinter run records for the React table
            var allRuns = await _sinterRunLogService.GetAllRunsAsync();
            return View(allRuns);
        }

        [HttpPost("StartSkid")]
        public IActionResult StartSkid(string operatorName, string part, string run, string furnace, string process, string notes)
        {
            // Validate inputs and start the new skid
            if (string.IsNullOrWhiteSpace(operatorName) || string.IsNullOrWhiteSpace(part) ||
                string.IsNullOrWhiteSpace(run) || string.IsNullOrWhiteSpace(furnace) ||
                string.IsNullOrWhiteSpace(process))
            {
                ViewData["Error"] = "All fields are required.";
                return RedirectToAction("Index");
            }

            try
            {
                // End any existing skids on the selected furnace if needed
                _sinterRunLogService.EndSkidsByFurnaceIfNeeded(furnace);
                // Start a new skid
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
                Console.WriteLine($"Closing Sinter Run: Part = {part}, Run = {run}");
                _sinterRunLogService.CloseSkid(part, run);
                ViewData["Message"] = $"Sintering stopped for {part} - {run} successfully.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Closing Sinter Run: {ex.Message}");
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
