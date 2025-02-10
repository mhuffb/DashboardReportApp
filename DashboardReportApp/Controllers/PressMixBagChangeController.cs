using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Collections.Generic;
using DashboardReportApp.Models;

public class PressMixBagChangeController : Controller
{
    private readonly PressMixBagChangeService _pressMixBagChangeService;

    public PressMixBagChangeController(PressMixBagChangeService databaseService)
    {
        _pressMixBagChangeService = databaseService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var equipment = await _pressMixBagChangeService.GetEquipmentAsync();
        var operators = await _pressMixBagChangeService.GetOperatorsAsync();
        var openPartsWithRuns = await _pressMixBagChangeService.GetOpenPartsWithRunsAsync(); // ✅ Fetch parts & runs
        var allRecords = await _pressMixBagChangeService.GetAllMixBagChangesAsync(); // Fetch all records

        ViewData["EquipmentList"] = equipment;
        ViewData["OperatorList"] = operators;
        ViewData["Parts"] = openPartsWithRuns;
        ViewData["AllRecords"] = allRecords;

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Submit(PressMixBagChangeModel model)
    {
        // Only validate when the form is submitted
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Invalid form submission. Please check required fields.";
            return RedirectToAction("Index");
        }

        try
        {
            // Debugging: Log received values
            Console.WriteLine($"Received: Part={model.Part}, Run={model.Run}, Operator={model.Operator}, Machine={model.Machine}, Lot={model.LotNumber}, Mix={model.MixNumber}, Notes={model.Notes}");

            // Insert into the database
            await _pressMixBagChangeService.InsertPressLotChangeAsync(
                model.Part,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                model.Operator,
                model.Machine,
                model.LotNumber,
                model.MixNumber,
                model.Notes
            );

            TempData["Success"] = "Mix Bag Change successfully logged!";
        }
        catch (Exception ex)
        {
            // Log the error
            Console.WriteLine($"Database Insert Error: {ex.Message}");
            TempData["Error"] = "An error occurred while saving the data. Please try again.";
        }

        return RedirectToAction("Index");
    }

}


