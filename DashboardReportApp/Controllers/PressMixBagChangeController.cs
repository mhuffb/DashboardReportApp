using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Collections.Generic;
using DashboardReportApp.Models;
using MySql.Data.MySqlClient;

public class PressMixBagChangeController : Controller
{
    private readonly PressMixBagChangeService _pressMixBagChangeService;

    public PressMixBagChangeController(PressMixBagChangeService databaseService)
    {
        _pressMixBagChangeService = databaseService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, int pageSize = 25)
    {
        var equipment = await _pressMixBagChangeService.GetEquipmentAsync();
        var operators = await _pressMixBagChangeService.GetOperatorsAsync();
        var openPartsWithRuns = await _pressMixBagChangeService.GetOpenPartsWithRunsAsync();

        var paged = await _pressMixBagChangeService.GetMixBagChangesPageAsync(page, pageSize);

        ViewData["EquipmentList"] = equipment;
        ViewData["OperatorList"] = operators;
        ViewData["Parts"] = openPartsWithRuns;

        // only the current page’s rows:
        ViewData["AllRecords"] = paged.Items;
        ViewData["Page"] = paged.Page;
        ViewData["PageSize"] = paged.PageSize;
        ViewData["TotalCount"] = paged.TotalCount;
        ViewData["TotalPages"] = paged.TotalPages;

        return View();
    }


    // PressMixBagChangeController.cs
    [HttpPost]
    public async Task<IActionResult> SubmitAjax(PressMixBagChangeModel model, string? overridePin = null)
    {
        string Normalize(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim().ToUpperInvariant();
            s = s.Replace(" ", "");
            while (s.Contains("--")) s = s.Replace("--", "-");
            return s;
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(new { code = "INVALID", message = "Invalid form submission." });
        }

        // Pull scheduled material
        var scheduledCode = await _pressMixBagChangeService.GetScheduledMaterialCodeAsync(
            model.Part ?? "", model.ProdNumber ?? "", model.Run ?? ""
        );
        var normSched = Normalize(scheduledCode);
        var normScan = Normalize(model.MaterialCode);

        var allowInsert = false;
        var supervisorName = "";

        if (string.IsNullOrEmpty(normSched))
        {
            // No schedule row found
            if (!string.IsNullOrEmpty(overridePin))
            {
                var (ok, supName) = await _pressMixBagChangeService.VerifySupervisorPinAsync(overridePin);
                if (!ok) return StatusCode(403, new { code = "BAD_PIN", message = "Invalid supervisor PIN." });

                allowInsert = true;
                supervisorName = supName;
            }
            else
            {
                return BadRequest(new
                {
                    code = "NO_SCHEDULE_ROW",
                    message = $"No schedule row found for Part {model.Part}, Component {model.Component} (Prod {model.ProdNumber}, Run {model.Run})."
                });
            }

        }
        else if (!normSched.Equals(normScan, StringComparison.Ordinal))
        {
            // Mismatch
            if (!string.IsNullOrEmpty(overridePin))
            {
                var (ok, supName) = await _pressMixBagChangeService.VerifySupervisorPinAsync(overridePin);
                if (!ok) return StatusCode(403, new { code = "BAD_PIN", message = "Invalid supervisor PIN." });
                allowInsert = true;
                supervisorName = supName;
            }
            else
            {
                return BadRequest(new
                {
                    code = "MATERIAL_MISMATCH",
                    scheduled = scheduledCode,
                    scanned = model.MaterialCode,
                    message = "Material does not match schedule."
                });
            }
        }
        else
        {
            // Match
            allowInsert = true;
        }

        if (allowInsert)
        {
            await _pressMixBagChangeService.InsertPressMixBagChangeAsync(
                model.Part,
                model.Component,
                model.ProdNumber,
                model.Run,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                model.Operator,
                model.Machine,
                model.LotNumber,
                model.MaterialCode,
                model.WeightLbs ?? 0m,
                model.BagNumber,
                model.Notes,
                isOverride: !string.IsNullOrEmpty(supervisorName),
                overrideBy: supervisorName,
                overrideAt: !string.IsNullOrEmpty(supervisorName) ? DateTime.Now : (DateTime?)null
            );

            return Ok(new { ok = true, overrideUsed = !string.IsNullOrEmpty(supervisorName), supervisor = supervisorName });
        }

        return BadRequest(new { code = "UNKNOWN", message = "Unhandled state." });
    }





    [HttpGet]
    public async Task<string> GetMaterialCodeByLot(string lot)
    {
        return await _pressMixBagChangeService.GetMaterialCodeByLotAsync(lot);
    }
    [HttpGet]
    public async Task<JsonResult> GetRunInfo(string part, string prod, string run)
    {
        var (op, mach) = await _pressMixBagChangeService.GetLatestRunInfoAsync(part, prod, run);
        return Json(new { op, mach });
    }

}


