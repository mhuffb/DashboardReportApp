﻿using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
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

        // ==================== LOGIN =====================
        // GET /PressRunLog/LoadLoginModal?machine=xxx
        [HttpGet]
        public async Task<IActionResult> LoadLoginModal(string machine)
        {
            int? deviceCount = await _moldingService.TryGetDeviceCountOrNull(machine);
            ViewBag.Machine = machine;
            ViewBag.DeviceCount = deviceCount ?? 0;

            Console.WriteLine("DeviceCount: " + deviceCount);
            Console.WriteLine("Machine: " + machine);
            return PartialView("_LoginCountModal");
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmLogin(PressRunLogModel model)
        {
            Console.WriteLine("Received ProdNumber: " + model.ProdNumber);
            Console.WriteLine("Received Component: " + model.Component);  // Debug component value

            var formModel = new PressRunLogModel
            {
                Operator = model.Operator,
                Part = model.Part,
                Component = model.Component,  // <-- Include the component!
                Machine = model.Machine,
                Run = model.Run,
                StartDateTime = DateTime.Now,
                ProdNumber = model.ProdNumber
            };
            await _pressRunLogService.HandleLogin(formModel);
            return RedirectToAction("Index");
        }


        // ==================== START SKID =====================
        // Updated action name and parameter names to match the modal form
        [HttpPost]
        public async Task<IActionResult> StartSkid(PressRunLogModel model, int pcsStart)
        {
            Console.WriteLine("Received Start Skid Request");
            Console.WriteLine($"Run ID: {model.Run}");
            Console.WriteLine($"Machine: {model.Machine}");
            Console.WriteLine($"Part: {model.Part}");
            Console.WriteLine($"Operator: {model.Operator}");
            Console.WriteLine($"Prod Number: {model.ProdNumber}");
            Console.WriteLine($"Pcs Start Received: {pcsStart}");

           
            await _pressRunLogService.HandleStartSkidAsync(model);


            Console.WriteLine("Start Skid Processed Successfully");
            return RedirectToAction("Index");
        }



        // ==================== LOGOUT =====================
        // GET /PressRunLog/LoadLogoutModal => user sees device count => typed final count
        [HttpGet]
        public async Task<IActionResult> LoadLogoutModal(int runId, string machine)
        {
            int? deviceCount = await _moldingService.TryGetDeviceCountOrNull(machine);
            ViewBag.RunId = runId;
            ViewBag.Machine = machine;
            ViewBag.DeviceCount = deviceCount ?? 0;
            return PartialView("_LogoutCountModal");
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmLogout(int runId, int finalCount, int scrap, string notes)
        {
            // Update the main run record with endDateTime, pcsEnd, scrap, and notes.
            await _pressRunLogService.HandleLogoutAsync(runId, finalCount, scrap, notes);
            return RedirectToAction("Index");
        }

        // ==================== END RUN =====================
        // GET /PressRunLog/LoadEndRunModal?runId=xxx&machine=xxx => partial
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
            // 1) Call your service to get device count or null
            int? count = await _moldingService.TryGetDeviceCountOrNull(machine);
            // 2) Return JSON
            return Json(new { deviceCount = count ?? 0 });
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmEndRun(int runId, int finalCount, int scrap, string notes)
        {
            await _pressRunLogService.HandleEndRunAsync(runId, finalCount, scrap, notes);
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> GenerateRouterTag(PressRunLogModel model)
        {

            string pdfFilePath = await _pressRunLogService.GenerateRouterTagAsync(model);


            string computerName = Environment.MachineName;
            Console.WriteLine("Computer Name: " + computerName);

            if(computerName == "Mold02")
            {
                _sharedService.PrintFile("Mold02", pdfFilePath);
            }
            else if(computerName == "Mold03")
            {
                _sharedService.PrintFile("Mold03", pdfFilePath);
            }
            else
            {

            }

            return RedirectToAction("Index");
        }
    }
}
