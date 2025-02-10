using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Collections.Generic;
using DashboardReportApp.Models;

public class PressMixBagChangeController : Controller
{
    private readonly PressMixBagChangeService _databaseService;

    public PressMixBagChangeController(PressMixBagChangeService databaseService)
    {
        _databaseService = databaseService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var equipment = await _databaseService.GetEquipmentAsync();
        var operators = await _databaseService.GetOperatorsAsync();
        var openPartsWithRuns = await _databaseService.GetOpenPartsWithRunsAsync(); // ✅ Fetch parts & runs

        ViewData["EquipmentList"] = equipment;
        ViewData["OperatorList"] = operators;
        ViewData["OpenPartsWithRuns"] = openPartsWithRuns;

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Submit(PressMixBagChangeModel form)
    {
        if (!ModelState.IsValid)
        {
            // Re-populate dropdowns if validation fails
            ViewData["EquipmentList"] = await _databaseService.GetEquipmentAsync();
            ViewData["OperatorList"] = await _databaseService.GetOperatorsAsync();
            ViewData["OpenPartsWithRuns"] = await _databaseService.GetOpenPartsWithRunsAsync();

            return View("Index");
        }

        // Process the data (Insert into the database)
        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        await _databaseService.InsertPressLotChangeAsync(
            form.Part,
            time,
            form.Operator,
            form.Machine,
            form.LotNumber,
            form.MixNumber,
            form.Notes
        );

        return RedirectToAction("Index");
    }
}
