﻿using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using static DashboardReportApp.Services.PressRunLogService;

namespace DashboardReportApp.Controllers
{
    
    public class PressRunLogController : Controller
    {
        private readonly PressRunLogService _pressRunLogService;
        private readonly SharedService _sharedService;
        private readonly MoldingService _moldingService;

        public PressRunLogController(PressRunLogService servicePressRun, SharedService serviceShared, MoldingService serviceMolding)
        {
            _pressRunLogService = servicePressRun;
            _sharedService = serviceShared;
            _moldingService = serviceMolding;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewData["Operators"] = await _pressRunLogService.GetOperatorsAsync();

            // openParts => for the Start Molding form
            ViewData["OpenParts"] = await _pressRunLogService.GetOpenSetups();

            var openRuns = await _pressRunLogService.GetLoggedInRunsAsync();
            var allRuns = await _pressRunLogService.GetAllRunsAsync();

            ViewBag.OpenRuns = openRuns;
            return View(allRuns); // the Index.cshtml
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
        public async Task<IActionResult> ConfirmLogin(PressRunLogModel model, int pcsStart)
        {
            model.PcsStart = pcsStart;
            model.StartDateTime = DateTime.Now;

            LoginResult res = await _pressRunLogService.HandleLoginAsync(model);

            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                          Request.Headers["Accept"].ToString().Contains("application/json");

            if (isAjax)
            {
                return Json(new { ok = true, message = res.Message });
            }

            TempData["Toast"] = res.Message;
            return RedirectToAction("Index");
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartSkid(PressRunLogModel model, int pcsStart)
        {
            StartSkidResult res = await _pressRunLogService.HandleStartSkidAsync(model, pcsStart);

            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                          Request.Headers["Accept"].ToString().Contains("application/json");

            if (isAjax)
            {
                return Json(new { ok = true, message = res.Message });
            }

            TempData["Toast"] = res.Message;
            return RedirectToAction("Index");
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
    }
}
