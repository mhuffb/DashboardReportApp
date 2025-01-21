using DashboardReportApp.Models;
using Microsoft.AspNetCore.Mvc;

public class SinteringController : Controller
{
    private readonly SinterRunLogService _sinterRunLogService;

    public SinteringController(SinterRunLogService sinterRunLogService)
    {
        _sinterRunLogService = sinterRunLogService;
    }

    public IActionResult Index()
    {
        var operators = _sinterRunLogService.GetOperators();
        // Ensure it's not null, if it is, assign an empty list
        ViewData["Operators"] = operators ?? new List<string>();

        var furnaces = _sinterRunLogService.GetFurnaces();
        ViewData["Furnaces"] = furnaces ?? new List<string>();

        var openSkids = _sinterRunLogService.GetOpenSkids();
        ViewData["OpenSkids"] = openSkids ?? new List<SinterRunSkid>();

        return View();
    }


    [HttpPost]
    public IActionResult StartSkid(string operatorName, string part, string furnace, string process, string notes)
    {
        if (string.IsNullOrWhiteSpace(operatorName) || string.IsNullOrWhiteSpace(part) || string.IsNullOrWhiteSpace(furnace) || string.IsNullOrWhiteSpace(process))
        {
            ViewData["Error"] = "All fields are required.";
            return RedirectToAction("Index");
        }

        try
        {
            // Check if a skid is already running on the selected furnace
            _sinterRunLogService.EndSkidsByFurnaceIfNeeded(furnace); // End any existing skid on the same furnace

            // Start the new skid
            _sinterRunLogService.StartSkid(operatorName, part, furnace, process, notes);
            ViewData["Message"] = $"{part} started successfully on Furnace {furnace}.";
        }
        catch (Exception ex)
        {
            ViewData["Error"] = $"An error occurred: {ex.Message}";
        }

        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult CloseSkid(string part, string furnace)
    {
        try
        {
            _sinterRunLogService.CloseSkidsByFurnace(furnace);
            _sinterRunLogService.CloseSkid(part, furnace);
            ViewData["Message"] = $"Skid {part} on Furnace {furnace} closed successfully.";
        }
        catch (Exception ex)
        {
            ViewData["Error"] = $"An error occurred: {ex.Message}";
        }

        return RedirectToAction("Index");
    }

    [HttpPost]
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
