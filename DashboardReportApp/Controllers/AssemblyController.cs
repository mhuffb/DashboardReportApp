﻿using DashboardReportApp.Models;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    [Route("AssemblyRun")]
    public class AssemblyController : Controller
    {
        private readonly AssemblyService _assemblyService;

        public AssemblyController(AssemblyService assemblyService)
        {
            _assemblyService = assemblyService;
        }

        public async Task<IActionResult> Index()
        {
            // 1) Get operators and furnaces from the service
            var operators = _assemblyService.GetOperators();
            ViewData["Operators"] = operators ?? new List<string>();


            // 2) Fetch open skids (where open = 1 and skidNumber > 0) from the pressrun table
            var openGreenSkids = await _assemblyService.GetOpenGreenSkidsAsync();
            ViewBag.OpenGreenSkids = openGreenSkids ?? new List<PressRunLogModel>();

            // 3) Create a dictionary of (Part, Run) => Furnace for dropdown selection from open skids
           var openParts = openGreenSkids.ToDictionary(
    r => (r.Part, r.Run, r.SkidNumber),
    r => r.Machine
);

            ViewData["OpenParts"] = openParts;
            foreach (var partRun in openParts)
            {
                Console.WriteLine($"Part: {partRun.Key.Item1}, Run: {partRun.Key.Item2}, Furnace: {partRun.Value}");
            }

            var openAssyG = await _assemblyService.GetScheduledAssyG();
            ViewData["OpenAssyG"] = openAssyG;

            // 4) Get all sinter run records for the React table
            var allRuns = await _assemblyService.GetAllRunsAsync();
            return View(allRuns);
        }

        [HttpPost("LoginToSkid")]
        public IActionResult LoginToSkid(AssemblyModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Operator) ||
                string.IsNullOrWhiteSpace(model.ProdNumber) ||
                string.IsNullOrWhiteSpace(model.Part) ||
                string.IsNullOrWhiteSpace(model.Run))
            {
                ViewData["Error"] = "All fields are required.";
                return RedirectToAction("Index");
            }

            try
            {
                _assemblyService.LoginToSkid(model);
            }
            catch (Exception ex)
            {
                ViewData["Error"] = $"An error occurred: {ex.Message}";
            }

            return RedirectToAction("Index");
        }


        [HttpPost("LogoutOfSkid")]
        public IActionResult LogoutOfSkid(string part, string run, string skidNumber)
        {
            try
            {
                Console.WriteLine($"Closing Sinter Run: Part = {part}, Run = {run}, SkidNumber = {skidNumber}");
                _assemblyService.LogoutOfSkid(part, run, skidNumber);
                ViewData["Message"] = $"Sintering stopped for {part} - {run} (Skid: {skidNumber}) successfully.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Closing Sinter Run: {ex.Message}");
                ViewData["Error"] = $"An error occurred: {ex.Message}";
            }
            return RedirectToAction("Index");
        }

        [HttpPost("EndSkid")]
        public IActionResult EndSkid(string prodNumber, string part, string skidNumber, string pcs,
                                        string run, string oper, string oven, string process, string notes)
        {
            try
            {
                _assemblyService.EndSkid(prodNumber, part, skidNumber, pcs, run, oper, oven, process, notes);
                ViewData["Message"] = "Skid run ended successfully.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Ending Skid Run: {ex.Message}");
                ViewData["Error"] = $"An error occurred: {ex.Message}";
            }
            return RedirectToAction("Index");
        }
        [HttpPost("EndGreenAssemblyRun")]
        public IActionResult EndSkid(string run, string part, string prodNumber)
        {
            try
            {
                _assemblyService.EndGreenAssemblyRun(run, part, prodNumber);
                ViewData["Message"] = "Skid run ended successfully.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Ending Skid Run: {ex.Message}");
                ViewData["Error"] = $"An error occurred: {ex.Message}";
            }
            return RedirectToAction("Index");
        }
    }
}
