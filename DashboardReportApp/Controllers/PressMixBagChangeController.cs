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
        if (string.IsNullOrWhiteSpace(model.ProdNumber) || string.IsNullOrWhiteSpace(model.Run))
        {
            var openParts = await _pressMixBagChangeService.GetOpenPartsWithRunsAsync();
            var match = openParts.FirstOrDefault(p => p.Part == model.Part);
            if (match != null)
            {
                model.ProdNumber = model.ProdNumber ?? match.ProdNumber;
                model.Run = model.Run ?? match.Run;
                ModelState["ProdNumber"].Errors.Clear();
                ModelState["Run"].Errors.Clear();
            }
        }
        // 🔹 SERVER-SIDE MATERIALCODE FILL FROM LOT, IF MISSING
        if (string.IsNullOrWhiteSpace(model.MaterialCode) && !string.IsNullOrWhiteSpace(model.LotNumber))
        {
            var lookedUp = await _pressMixBagChangeService.GetMaterialCodeByLotAsync(model.LotNumber);
            if (!string.IsNullOrWhiteSpace(lookedUp))
            {
                model.MaterialCode = lookedUp;

                // clear any ModelState error for MaterialCode if it was [Required]
                if (ModelState.ContainsKey(nameof(model.MaterialCode)))
                {
                    ModelState[nameof(model.MaterialCode)].Errors.Clear();
                }
            }
        }
        if (!ModelState.IsValid)
        {
            var errorList = ModelState
                .Where(kvp => kvp.Value.Errors.Count > 0)
                .Select(kvp => new
                {
                    field = kvp.Key,
                    errors = kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray(),
                    attemptedValue = kvp.Value.AttemptedValue
                });

            return BadRequest(new
            {
                code = "INVALID",
                message = "Invalid form submission.",
                details = errorList
            });
        }


        // Pull scheduled material
        var scheduledCode = await _pressMixBagChangeService.GetScheduledMaterialCodeAsync(
            model.Part ?? "", model.ProdNumber ?? "", model.Run ?? ""
        );
        var normSched = Normalize(scheduledCode);
        var normScan = Normalize(model.MaterialCode);

        var allowInsert = false;
        var supervisorName = "";

        // 🔹 Check for existing override in *either* table
        var (hasOverride, existingSup) = await _pressMixBagChangeService.HasExistingOverrideAsync(
            model.Part ?? "", model.ProdNumber ?? "", model.Run ?? ""
        );

        if (string.IsNullOrEmpty(normSched))
        {
            // No schedule row found
            if (hasOverride)
            {
                // ✅ Already overridden earlier, don't ask again
                allowInsert = true;
                supervisorName = existingSup;
            }
            else if (!string.IsNullOrEmpty(overridePin))
            {
                var (ok, supName) = await _pressMixBagChangeService.VerifySupervisorPinAsync(overridePin);
                if (!ok) return StatusCode(403, new { code = "BAD_PIN", message = "Invalid supervisor PIN." });

                allowInsert = true;
                supervisorName = supName;
            }
            else
            {
                // ❌ Tell frontend we need override (PIN)
                return BadRequest(new
                {
                    code = "NO_SCHEDULE_ROW",
                    message = $"No schedule row found for Part {model.Part}, Component {model.Component} (Prod {model.ProdNumber}, Run {model.Run})."
                });
            }
        }
        else if (!normSched.Equals(normScan, StringComparison.Ordinal))
        {
            // MATERIAL MISMATCH
            if (hasOverride)
            {
                // ✅ Already overridden earlier, don't ask again
                allowInsert = true;
                supervisorName = existingSup;
            }
            else if (!string.IsNullOrEmpty(overridePin))
            {
                var (ok, supName) = await _pressMixBagChangeService.VerifySupervisorPinAsync(overridePin);
                if (!ok) return StatusCode(403, new { code = "BAD_PIN", message = "Invalid supervisor PIN." });

                allowInsert = true;
                supervisorName = supName;
            }
            else
            {
                // ❌ Tell frontend we need override (PIN)
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
            // Match -> no override needed
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
    [HttpGet]
    public IActionResult ApiGetMaterialCode(string machine)
    {
        if (string.IsNullOrWhiteSpace(machine))
            return Json(new { materialCode = (string)null });

        var materialCode = _pressMixBagChangeService.GetLatestMaterialCodeForMachine(machine);
        return Json(new { materialCode });
    }

}


