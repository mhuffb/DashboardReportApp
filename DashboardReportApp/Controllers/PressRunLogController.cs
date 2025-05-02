using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

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
        public async Task<IActionResult> ConfirmLogin(PressRunLogModel model, int pcsStart)
        {
            var formModel = new PressRunLogModel
            {
                Operator = model.Operator,
                Part = model.Part,
                Component = model.Component,
                Machine = model.Machine,
                Run = model.Run,
                StartDateTime = DateTime.Now,
                ProdNumber = model.ProdNumber,
                PcsStart = pcsStart
            };

            await _pressRunLogService.HandleLogin(formModel);
            return RedirectToAction("Index");
        }



        // ============== START SKID ==============
        [HttpPost]
        public async Task<IActionResult> StartSkid(PressRunLogModel model, int pcsStart)
        {
            // This will end the previous skid if open, auto-print its tag,
            // then start the new skid, auto-print its tag.
            await _pressRunLogService.HandleStartSkidAsync(model);
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
        public async Task<IActionResult> ConfirmLogout(int runId, int finalCount, int scrap, string notes)
        {
            await _pressRunLogService.HandleLogoutAsync(runId, finalCount, scrap, notes);
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
        public async Task<IActionResult> ConfirmEndRun(int runId, int finalCount, int scrap, string notes, bool orderComplete, string machine)
        {
            // Ends main run and automatically ends any open skid(s) for that run
            await _pressRunLogService.HandleEndRunAsync(runId, finalCount, scrap, notes, orderComplete);
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
                    using var http = new HttpClient();
                    var content = new FormUrlEncodedContent(new[]
                    {
                new KeyValuePair<string, string>("count_value", "0")
            });

                    try
                    {
                        await http.PostAsync($"http://{ip}/update", content);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to reset device count for machine {machine}: {ex.Message}");
                    }
                }
            }

            return RedirectToAction("Index");
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
