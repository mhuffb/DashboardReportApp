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
        public async Task<IActionResult> ConfirmLogin(PressRunLogModel model)
        {
            var formModel = new PressRunLogModel
            {
                Operator = model.Operator,
                Part = model.Part,
                Component = model.Component,
                Machine = model.Machine,
                Run = model.Run,
                StartDateTime = DateTime.Now,
                ProdNumber = model.ProdNumber
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
            return PartialView("_LogoutCountModal");
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmLogout(int runId, int finalCount, int scrap, string notes)
        {
            // Logs out main run, does not forcibly end the run, but sets endDateTime for that operator
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
        public async Task<IActionResult> ConfirmEndRun(int runId, int finalCount, int scrap, string notes, bool orderComplete)
        {
            // Ends main run and automatically ends any open skid(s) for that run
            await _pressRunLogService.HandleEndRunAsync(runId, finalCount, scrap, notes, orderComplete);
            return RedirectToAction("Index");
        }


        // ============== MANUAL PRINT TAG (BUTTON) ==============
        [HttpPost]
        public async Task<IActionResult> GenerateRouterTag(PressRunLogModel model)
        {
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Optional: Reverse lookup to get a host name, if your network DNS is set up
             string clientHostName = Dns.GetHostEntry(clientIp).HostName;
            Console.WriteLine("Client IP: " + clientIp + "ClientHostName: " + clientHostName);
            

            // Prepare the log message. Adjust the text if needed.
            string textToWrite = $"{DateTime.Now}: User name is {clientHostName} Ip is {clientIp}";

            // Define the shared file path
            string filePath = @"\\Sintergydc2024\vol1\vsp\testcomputername.txt";

            try
            {
                // Append the text to the file (creates the file if it doesn't exist)
                System.IO.File.AppendAllText(filePath, textToWrite + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // Consider proper logging or error handling here
                // For example, log the error to a logging service or a local file if file access fails
                throw new Exception("Error writing to log file", ex);
            }

            // The user can manually click to print a tag for the skid.
            string pdfFilePath = await _pressRunLogService.GenerateRouterTagAsync(model);
            _sharedService.PrintFileToClosestPrinter(pdfFilePath);
          
            return RedirectToAction("Index");
        }
    }
}
