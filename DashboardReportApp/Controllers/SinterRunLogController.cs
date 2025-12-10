using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardReportApp.Controllers
{
    [Route("SinterRunLog")]
    public class SinterRunLogController : Controller
    {
        private readonly SinterRunLogService _sinterRunLogService;
        private readonly SharedService _sharedService;

        public SinterRunLogController(
            SinterRunLogService sinterRunLogService,
            SharedService sharedService)
        {
            _sinterRunLogService = sinterRunLogService;
            _sharedService = sharedService;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(int page = 1, int pageSize = 50, string? search = null, string? sort = "id", string? dir = "DESC")
        {
            // Aux data
            var operators = _sharedService.GetFormattedOperators();
            var furnaces = _sharedService.GetFurnaces();

            var openGreenSkids = await _sinterRunLogService.GetOpenGreenSkidsAsync();
            var openSinterRuns = await _sinterRunLogService.GetOpenSinterRunsAsync();

            // Paged data for the big table
            var total = await _sinterRunLogService.GetRunsCountAsync(search);
            var items = await _sinterRunLogService.GetRunsPageAsync(page, pageSize, sort, dir, search);

            var vm = new SinterRunLogViewModel
            {
                // ✅ hook it up here
                Operators = operators ?? new List<string>(),
                Furnaces = furnaces ?? new List<string>(),
                OpenGreenSkids = openGreenSkids ?? new List<PressRunLogModel>(),
                OpenSinterRuns = openSinterRuns ?? new List<SinterRunSkid>(),
                PageItems = items,
                Page = page,
                PageSize = pageSize,
                Total = total,
                Search = search,
                Sort = sort,
                Dir = dir
            };

            return View(vm);
        }


        // JSON data endpoint for the React table (optional, if you want client fetch)
        [HttpGet("Data")]
        public async Task<IActionResult> Data(int page = 1, int pageSize = 50, string? search = null, string? sort = "id", string? dir = "DESC")
        {
            var total = await _sinterRunLogService.GetRunsCountAsync(search);
            var items = await _sinterRunLogService.GetRunsPageAsync(page, pageSize, sort, dir, search);
            return Json(new { items, page, pageSize, total });
        }


        [HttpPost("LoginToSkid")]
        public IActionResult LoginToSkid(SinterRunSkid model)
        {
            if (string.IsNullOrWhiteSpace(model.Operator) ||
    string.IsNullOrWhiteSpace(model.ProdNumber) ||
    string.IsNullOrWhiteSpace(model.Part) ||
    string.IsNullOrWhiteSpace(model.Machine) ||
    string.IsNullOrWhiteSpace(model.Process))
            {
                ViewData["Error"] = "All required fields are required.";
                return RedirectToAction("Index");
            }


            try
            {
                _sinterRunLogService.EndSkidsByMachineIfNeeded(model.Machine);
                _sinterRunLogService.LoginToSkid(model);
                ViewData["Message"] = $"{model.Part} - {model.Run} started successfully on Furnace {model.Machine}.";
            }
            catch (Exception ex)
            {
                ViewData["Error"] = $"An error occurred: {ex.Message}";
            }

            return RedirectToAction("Index");
        }


        [HttpPost("LogoutOfSkid")]
        public IActionResult LogoutOfSkid(string part, string run, string skidNumber, string prodNumber)
        {
            try
            {
                Console.WriteLine($"Closing Sinter Run: Part = {part}, Run = {run}, SkidNumber = {skidNumber}");
                _sinterRunLogService.LogoutOfSkid(part, run, skidNumber, prodNumber);
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
                _sinterRunLogService.EndSkid(prodNumber, part, skidNumber, pcs, run, oper, oven, process, notes);
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
