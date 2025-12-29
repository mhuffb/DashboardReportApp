using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using static DashboardReportApp.Services.PressRunLogService;
using MySql.Data.MySqlClient;
using System.Data;
using System.Linq;


namespace DashboardReportApp.Controllers
{
    
    public class PressRunLogController : Controller
    {
        private readonly PressRunLogService _pressRunLogService;
        private readonly SharedService _sharedService;
        private readonly MoldingService _moldingService;
        private readonly IConfiguration _config;


        public PressRunLogController(
    PressRunLogService servicePressRun,
    SharedService serviceShared,
    MoldingService serviceMolding,
    IConfiguration config) 
        {
            _pressRunLogService = servicePressRun;
            _sharedService = serviceShared;
            _moldingService = serviceMolding;
            _config = config; 
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewData["Operators"] = _sharedService.GetFormattedOperators();
            ViewData["OpenParts"] = await _pressRunLogService.GetOpenSetups();
            ViewBag.OpenRuns = await _pressRunLogService.GetLoggedInRunsAsync();

            // 🔹 New: all runs for DataTables HTML table
            //  ViewData["AllRuns"] = await _pressRunLogService.GetAllRunsAsync();

            ViewBag.HoldKeys = await _pressRunLogService.GetOpenHoldKeysAsync("pressrun");
            return View();
        }



        // ============== LOGIN ==============
        [HttpGet]
        public async Task<IActionResult> LoadLoginModal(string machine)
        {
            int? deviceCount = await _moldingService.TryGetDeviceCountOrNull(machine);
            ViewBag.Machine = machine;
            ViewBag.DeviceCount = deviceCount ?? 0;
            return PartialView("_LoginCountModal");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmLogin(PressRunLogModel model, int pcsStart, string? overridePin = null, string? overrideReason = null)

        {
            // Detect whether this is the Ajax flow (our hookOverrideForm) or a normal post
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                          Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

            try
            {
                model.PcsStart = pcsStart;
                model.StartDateTime = DateTime.Now;

                LoginResult res = await _pressRunLogService.HandleLoginAsync(model, overridePin, overrideReason);

                // ===================== EMAIL: OVERRIDE USED (LOGIN) =====================
                try
                {
                    // only send when a PIN was actually supplied (i.e., a new supervisor override)
                    if (!string.IsNullOrWhiteSpace(overridePin))
                    {
                        var to = _config["Email:PressRunOverrideNotifyTo"] ?? "";
                        

                        if (!string.IsNullOrWhiteSpace(to))
                        {
                            var subj = "Override used in Press Run Log";

                            var body =
                            $@"Material Override used in Press Run Log

Action: LOGIN

Part: {model.Part}
Component: {model.Component}

Prod/Run: {model.ProdNumber} / {model.Run}
Machine: {model.Machine}
Operator: {model.Operator}

Scheduled: {res.ScheduledMaterial}
Scanned:   {res.ScannedMaterial}

Lot: {model.LotNumber}
Bag: 
Weight: 

Supervisor: {res.Supervisor}
Reason: {overrideReason}
Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";


                            // NOTE: your method signature:
                            // SendEmailWithAttachment(receiverEmail, attachmentPath, attachmentPath2, subject, body)
                            _sharedService.SendEmailWithAttachment(to, "", "", subj, body);

                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Override email failed (LOGIN): " + ex.Message);
                }

                // 🔸 Supervisor override / material mismatch case
                if (res.RequiresOverride)
                {
                    if (isAjax)
                    {
                        // 400 → our JS sees this and pops the PIN SweetAlert
                        return StatusCode(400, new
                        {
                            ok = false,
                            code = string.IsNullOrWhiteSpace(res.Code) ? "SUPERVISOR_REQUIRED" : res.Code,
                            message = res.Message,
                            scheduled = res.ScheduledMaterial,
                            scanned = res.ScannedMaterial
                        });
                    }

                    TempData["Error"] = res.Message;
                    return RedirectToAction("Index");
                }

                // ✅ Success
                if (isAjax)
                {
                    return Json(new
                    {
                        ok = true,
                        message = string.IsNullOrWhiteSpace(res.Message)
                                    ? $"Logged in to run {model.Run} on machine {model.Machine}."
                                    : res.Message
                    });
                }

                TempData["Toast"] = res.Message;
                return RedirectToAction("Index");
            }
            catch (Exception)
            {
                // IMPORTANT: do NOT leak ex.Message back to JS
                if (isAjax)
                {
                    return StatusCode(500, new
                    {
                        ok = false,
                        code = "SERVER_ERROR",
                        message = "An unexpected error occurred while logging in."
                    });
                }

                TempData["Error"] = "An unexpected error occurred while logging in.";
                return RedirectToAction("Index");
            }
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartSkid(PressRunLogModel model, int pcsStart, string? overridePin = null, string? overrideReason = null)

        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                          Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

            try
            {
                model.PcsStart = pcsStart;

                StartSkidResult res = await _pressRunLogService.HandleStartSkidAsync(model, pcsStart, overridePin, overrideReason);

                // ===================== EMAIL: OVERRIDE USED (START SKID) =====================
                try
                {
                    if (!string.IsNullOrWhiteSpace(overridePin))
                    {
                        var to = _config["Email:PressRunOverrideTo"] ?? "";
                        var overrideAll = _config["Email:OverrideAllTo"] ?? "";
                        if (!string.IsNullOrWhiteSpace(overrideAll))
                            to = overrideAll;

                        if (!string.IsNullOrWhiteSpace(to))
                        {
                            var subj = "Override used in Press Run Log";

                            var body =
                            $@"Material Override used in Press Run Log

Action: START SKID

Part: {model.Part}
Component: {model.Component}

Prod/Run: {model.ProdNumber} / {model.Run}
Machine: {model.Machine}
Operator: {model.Operator}
PcsStart: {pcsStart}

Scheduled: {res.ScheduledMaterial}
Scanned:   {res.ScannedMaterial}

Lot: {model.LotNumber}
Bag: 
Weight: 

Supervisor: {res.Supervisor}
Reason: {overrideReason}
Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";


                            _sharedService.SendEmailWithAttachment(to, "", "", subj, body);

                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Override email failed (START SKID): " + ex.Message);
                }

                // 🔸 Supervisor override / material mismatch case
                if (res.RequiresOverride)
                {
                    if (isAjax)
                    {
                        return StatusCode(400, new
                        {
                            ok = false,
                            code = string.IsNullOrWhiteSpace(res.Code) ? "SUPERVISOR_REQUIRED" : res.Code,
                            message = res.Message,
                            scheduled = res.ScheduledMaterial,
                            scanned = res.ScannedMaterial
                        });
                    }

                    TempData["Error"] = res.Message;
                    return RedirectToAction("Index");
                }

                // ✅ Success
                if (isAjax)
                {
                    return Json(new
                    {
                        ok = true,
                        message = string.IsNullOrWhiteSpace(res.Message)
                                    ? $"Started new skid for run {model.Run} on machine {model.Machine} at {pcsStart} pcs."
                                    : res.Message
                    });
                }

                TempData["Toast"] = res.Message;
                return RedirectToAction("Index");
            }
            catch (Exception)
            {
                if (isAjax)
                {
                    return StatusCode(500, new
                    {
                        ok = false,
                        code = "SERVER_ERROR",
                        message = "An unexpected error occurred while starting a skid."
                    });
                }

                TempData["Error"] = "An unexpected error occurred while starting a skid.";
                return RedirectToAction("Index");
            }
        }



        // ============== LOGOUT ==============
        [HttpGet]
        public async Task<IActionResult> LoadLogoutModal(int runId, string machine)
        {
            int? deviceCount = await _moldingService.TryGetDeviceCountOrNull(machine);
            ViewBag.RunId = runId;
            ViewBag.Machine = machine;
            ViewBag.DeviceCount = deviceCount ?? 0;
            return PartialView("_LogoutCountModal"); // we’ll create this modal next
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmLogout(int runId, int finalCount, int scrap, string notes)
        {
            await _pressRunLogService.HandleLogoutAsync(runId, finalCount, scrap, notes);

            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                          Request.Headers["Accept"].ToString().Contains("application/json");

            if (isAjax)
            {
                return Json(new { ok = true, message = "Logged out successfully." });
            }

            TempData["Toast"] = "Logged out successfully.";
            return RedirectToAction("Index");
        }



        // ============== END RUN ==============
        [HttpGet]
        public async Task<IActionResult> LoadEndRunModal(int runId, string machine)
        {
            int? deviceCount = await _moldingService.TryGetDeviceCountOrNull(machine);
            ViewBag.RunId = runId;
            ViewBag.Machine = machine;
            ViewBag.DeviceCount = deviceCount ?? 0;
            return PartialView("_EndRunCountModal");
        }

        [HttpGet]
        public async Task<IActionResult> ApiGetDeviceCount(string machine)
        {
            int? count = await _moldingService.TryGetDeviceCountOrNull(machine);
            return Json(new { deviceCount = count ?? 0 });
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmEndRun(string run, int finalCount, int scrap, string notes, bool orderComplete, string machine)
        {
            // Ends main run and any open skids, prints tags, and updates presssetup
            await _pressRunLogService.HandleEndRunAsync(run, finalCount, scrap, notes, orderComplete);

            // If order is complete, reset device counter to 0
            if (orderComplete)
            {
                var deviceIPs = new Dictionary<string, string>
                {
                    ["1"] = "192.168.1.30",
                    ["2"] = "192.168.1.31",
                    ["41"] = "192.168.1.32",
                    ["45"] = "192.168.1.33",
                    ["50"] = "192.168.1.34",
                    ["51"] = "192.168.1.35",
                    ["57"] = "192.168.1.36",
                    ["59"] = "192.168.1.37",
                    ["70"] = "192.168.1.38",
                    ["74"] = "192.168.1.39",
                    ["92"] = "192.168.1.40",
                    ["95"] = "192.168.1.41",
                    ["102"] = "192.168.1.42",
                    ["112"] = "192.168.1.43",
                    ["124"] = "192.168.1.44",
                    ["125"] = "192.168.1.45",
                    ["154"] = "192.168.1.46",
                    ["156"] = "192.168.1.47",
                    ["175"] = "192.168.1.48"
                };

                if (deviceIPs.TryGetValue(machine, out var ip))
                {
                    try
                    {
                        using var http = new HttpClient();
                        var content = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string, string>("count_value", "0")
                });
                        await http.PostAsync($"http://{ip}/update", content);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to reset count for machine {machine}: {ex.Message}");
                    }
                }
            }

            // Return JSON for frontend SweetAlert + reload
            return Json(new { ok = true, message = "Run ended successfully." });
        }


        // ============== MANUAL PRINT TAG (BUTTON) ==============
        [HttpPost]
        public async Task<IActionResult> GenerateRouterTag(PressRunLogModel model)
        {
          

            // The user can manually click to print a tag for the skid.
            string pdfFilePath = await _pressRunLogService.GenerateRouterTagAsync(model);
            _sharedService.PrintFileToClosestPrinter(pdfFilePath, 1);
          
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> ApiRuns(
       int page = 1,
       int pageSize = 100,
       string q = null,
       string machine = null,
       DateTime? start = null,
       DateTime? end = null)
        {
            var result = await _pressRunLogService.GetRunsPagedAsync(page, pageSize, q, machine, start, end);
            var holdKeys = await _pressRunLogService.GetOpenHoldKeysAsync("pressrun");

            // ✅ rows now use DB-stored pcs, durationHours, runDate
            var rows = result.Rows.Select(r =>
            {
                int? pcs = r.Pcs;
                decimal? durationHours = r.DurationHours;

                // Optional derived cycle time (sec/pc) using stored durationHours + stored pcs
                double? cycleSec = (durationHours.HasValue && pcs.HasValue && pcs.Value > 0)
                    ? Math.Round(((double)durationHours.Value * 3600.0) / pcs.Value, 2)
                    : (double?)null;

                bool onHold = false;
                if (r.SkidNumber > 0)
                {
                    var key = HoldKeyHelper.HoldKey("pressrun", r.ProdNumber, r.Run, r.Part, r.SkidNumber);
                    onHold = holdKeys.Contains(key);
                }



                return new
                {
                    id = r.Id,
                    part = r.Part,
                    component = r.Component,
                    prodNumber = r.ProdNumber,
                    run = r.Run,
                    machine = r.Machine,

                    lotNumber = r.LotNumber,
                    materialCode = r.MaterialCode,

                    @operator = r.Operator,
                    overrideBy = r.OverrideBy,
                    isOverride = r.IsOverride,
                    scheduledMaterial = r.ScheduledMaterial,

                    skidNumber = r.SkidNumber,

                    // ✅ NEW: from DB
                    runDate = r.RunDate,
                    durationHours = r.DurationHours,
                    pcs = r.Pcs,

                    scrap = r.Scrap,
                    notes = r.Notes,

                    // optional
                    cycleTime = cycleSec,
                    onHold = onHold
                };
            }).ToList();

            return Json(new { rows, total = result.Total, page = result.Page, pageSize = result.PageSize });
        }



        [HttpGet]
        public async Task<IActionResult> ApiMachines()
        {
            var machines = await _pressRunLogService.GetMachinesAsync();
            return Json(machines);
        }

    }
}
