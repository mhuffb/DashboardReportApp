using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using DashboardReportApp.Models;
public class PressMixBagChangeController : Controller
{
    private readonly PressLotChangeService _databaseService;

    public PressMixBagChangeController(PressLotChangeService databaseService)
    {
        _databaseService = databaseService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var equipment = await _databaseService.GetEquipmentAsync();
        var operators = await _databaseService.GetOperatorsAsync();

        var model = new PressMixBagChangeViewModel
        {
            EquipmentList = equipment,
            OperatorList = operators
        };

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Submit(PressMixBagChangeForm form)
    {
        if (!ModelState.IsValid)
        {
            // Re-populate dropdowns if validation fails
            var equipment = await _databaseService.GetEquipmentAsync();
            var operators = await _databaseService.GetOperatorsAsync();

            var model = new PressMixBagChangeViewModel
            {
                EquipmentList = equipment,
                OperatorList = operators
            };

            return View("Index", model);
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
            form.Note,
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
