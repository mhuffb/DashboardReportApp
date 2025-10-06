using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.IO;
namespace DashboardReportApp.Controllers
{
    [Route("AssemblyRun")]
    public class AssemblyController : Controller
    {
        private readonly AssemblyService _assemblyService;
        private readonly SharedService _sharedService;
        public AssemblyController(AssemblyService assemblyService, SharedService serviceShared)
        {
            _assemblyService = assemblyService;
            _sharedService = serviceShared;
        }

        public async Task<IActionResult> Index(string search, int page = 1, int pageSize = 50, string sort = "id", string dir = "DESC")
        {
            var operators = _assemblyService.GetOperators();
            var openGreenSkids = await _assemblyService.GetOpenGreenSkidsAsync();

            var (pageItems, total) = await _assemblyService.GetPagedRunsAsync(page, pageSize, sort, dir, search);

            var vm = new AssemblyRunViewModel
            {
                PageItems = pageItems,
                OpenGreenSkids = openGreenSkids,
                Page = page,
                PageSize = pageSize,
                Total = total,
                Search = search,
                Sort = sort,
                Dir = dir
            };

            ViewData["Operators"] = operators;

            return View(vm);
        }


        [HttpPost("LogSkid")]
        public async Task<IActionResult> LoginToSkidAsync(AssemblyModel model)
        {
            // Debug: Log received values.
            Console.WriteLine($"[DEBUG] LoginToSkid called with: Operator={model.Operator}, ProdNumber={model.ProdNumber}, Part={model.Part}, Pcs={model.Pcs}");

            if (string.IsNullOrWhiteSpace(model.Operator) ||
                string.IsNullOrWhiteSpace(model.ProdNumber) ||
                string.IsNullOrWhiteSpace(model.Part))
            {
                Console.WriteLine("[DEBUG] Validation failed: One or more required fields are missing.");
                ViewData["Error"] = "All fields are required.";
                return RedirectToAction("Index");
            }

            try
            {
                // Map the view model to your AssemblyModel
                var assemblyModel = new AssemblyModel
                {
                    ProdNumber = model.ProdNumber,
                    Part = model.Part,
                    Operator = model.Operator,
                    Pcs = model.Pcs,
                    Notes = model.Notes  // In case you want to capture notes as well.
                }; 

                // Log the skid and retrieve the new ID.
                await _assemblyService.LogSkidAsync(assemblyModel);

                // Now generate the PDF report using the updated model.
                string reportFileName = await _assemblyService.GenerateAssemblyReportAsync(assemblyModel);

                // Print still uses absolute:
                var abs = _assemblyService.GetExportAbsolutePath(reportFileName);
                _sharedService.PrintFileToClosestPrinter(abs, 1);

            }
            catch (Exception ex)
            {
                ViewData["Error"] = $"An error occurred: {ex.Message}";
            }

            return RedirectToAction("Index");
        }



        [HttpPost("EndProduction")]
        public IActionResult EndProduction(string part, string prodNumber)
        {
            try
            {
                _assemblyService.EndProduction(part, prodNumber);
                ViewData["Message"] = "Skid run ended successfully.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Ending Skid Run: {ex.Message}");
                ViewData["Error"] = $"An error occurred: {ex.Message}";
            }
            return RedirectToAction("Index");
        }

        

[HttpGet("FetchReport")]
    public IActionResult FetchReport(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Json(new { success = false, message = "No file name provided." });

        var abs = _assemblyService.GetExportAbsolutePath(name);
        if (string.IsNullOrEmpty(abs) || !System.IO.File.Exists(abs))
            return Json(new { success = false, message = $"File not found: {name}" });

        var url = Url.Action(nameof(StreamReport), "Assembly", new { name }, Request.Scheme);
        return Json(new { success = true, url });
    }

    [HttpGet("StreamReport")]
    public IActionResult StreamReport(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return NotFound();

        var abs = _assemblyService.GetExportAbsolutePath(name);
        if (string.IsNullOrEmpty(abs) || !System.IO.File.Exists(abs)) return NotFound();

        // these are always PDFs here
        return PhysicalFile(abs, "application/pdf", enableRangeProcessing: true);
    }

}
}
