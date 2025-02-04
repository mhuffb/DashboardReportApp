using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
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

        ViewData["EquipmentList"] = equipment;
        ViewData["OperatorList"] = operators;

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
            form.MixNumber, // Include Mix Number
            form.Notes,
            form.SupplierItemNumber
        );

        return RedirectToAction("Index");
    }


    private async Task<string> GetMixValueAsync(string supplierItemNumber)
    {
        // Add logic for fetching mix value from the database.
        return "MixValue";
    }
}
