using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.IO;


namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")]
    public class ToolingInventoryController : Controller
    {
        private readonly ToolingInventoryService _svc;
        private readonly ToolingWorkOrderService _history;
        private readonly IConfiguration _config;
        private readonly SharedService _shared;

        public ToolingInventoryController(
            ToolingInventoryService svc,
            ToolingWorkOrderService history,
            IConfiguration config,
            SharedService shared)
        {
            _svc = svc;
            _history = history;
            _config = config;
            _shared = shared;
        }

        // List
        // Controllers/ToolingInventoryController.cs

        public async Task<IActionResult> Index(string? asm = null)
        {
            var items = await _svc.GetAllAsync();

            var inProgress = _history.GetToolingWorkOrders()
                .Where(h => !h.DateReceived.HasValue)
                .OrderByDescending(h => h.Id)
                .ToList();
            ViewBag.ToolingInProgress = inProgress;

            // presets for type-ahead
            ViewBag.ToolItemList = await _svc.GetDistinctToolItemsAsync();

            // this will be used to prefill the search box
            ViewBag.InitialSearch = asm;

            return View(items);
        }


        [HttpGet]
        public async Task<IActionResult> Create(string? assembly = null)
        {
            ViewBag.ToolItemList = await _svc.GetDistinctToolItemsAsync();

            // 👇 NEW: who gets notified for Questionable tooling
            ViewBag.QuestionableNotifyTo =
                _config["Email:OverrideAllTo"]
                ?? _config["Tooling:QuestionableToolingTo"]
                ?? _config["Tooling:WorkOrderRequestTo"];

            var model = new ToolItemModel
            {
                AssemblyNumber = assembly?.Trim() ?? string.Empty,
                ToolItem = "",
                Condition = ToolCondition.ReadyForProduction,
                Status = ToolStatus.Available
            };
            return PartialView("_ToolItemForm", model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            ViewBag.ToolItemList = await _svc.GetDistinctToolItemsAsync();

            // 👇 SAME HERE
            ViewBag.QuestionableNotifyTo =
                _config["Email:OverrideAllTo"]
                ?? _config["Tooling:QuestionableToolingTo"]
                ?? _config["Tooling:WorkOrderRequestTo"];

            var model = await _svc.GetByIdAsync(id);
            if (model is null) return NotFound("Tooling item not found.");
            return PartialView("_ToolItemForm", model);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ToolItemModel model, string? mode = null)
        {
            model.AssemblyNumber = model.AssemblyNumber?.Trim() ?? "";
            model.ToolNumber = model.ToolNumber?.Trim() ?? "";
            model.ToolItem = model.ToolItem?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(model.AssemblyNumber))
                ModelState.AddModelError(nameof(model.AssemblyNumber), "Assembly # is required.");
            if (string.IsNullOrWhiteSpace(model.ToolNumber))
                ModelState.AddModelError(nameof(model.ToolNumber), "Tool Number is required.");
            if (string.IsNullOrWhiteSpace(model.ToolItem))
                ModelState.AddModelError(nameof(model.ToolItem), "Tool Item is required.");

            if (!ModelState.IsValid)
                return PartialView("_ToolItemForm", model);

            // First create the record (without attachment path)
            var newId = await _svc.CreateAsync(model);
            model.Id = newId;

            // Save attachment file (if any) using the pattern fileAttachment1_<id>
            if (model.FileAttachment1Upload != null)
            {
                model.FileAttachment1 = SaveToolAttachmentFile(model.FileAttachment1Upload, newId);
                await _svc.UpdateAsync(model);
            }

            var which = string.Equals(mode, "add_more", StringComparison.OrdinalIgnoreCase)
                ? "add_more"
                : "add_clear";

            return Json(new
            {
                ok = true,
                id = newId,
                mode = which,
                assembly = model.AssemblyNumber,
                message = $"Added tool {model.ToolNumber} to assembly {model.AssemblyNumber}."
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ToolItemModel model)
        {
            model.AssemblyNumber = model.AssemblyNumber?.Trim() ?? "";
            model.ToolNumber = model.ToolNumber?.Trim() ?? "";
            model.ToolItem = model.ToolItem?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(model.AssemblyNumber))
                ModelState.AddModelError(nameof(model.AssemblyNumber), "Assembly # is required.");
            if (string.IsNullOrWhiteSpace(model.ToolNumber))
                ModelState.AddModelError(nameof(model.ToolNumber), "Tool Number is required.");
            if (string.IsNullOrWhiteSpace(model.ToolItem))
                ModelState.AddModelError(nameof(model.ToolItem), "Tool Item is required.");

            if (!ModelState.IsValid)
                return PartialView("_ToolItemForm", model);

            var existing = await _svc.GetByIdAsync(model.Id);
            if (existing is null) return NotFound("Tooling item not found.");

            var oldCondition = existing.Condition;

            // Keep existing attachment path unless a new file is uploaded
            model.FileAttachment1 = existing.FileAttachment1;

            if (model.FileAttachment1Upload != null)
            {
                model.FileAttachment1 = SaveToolAttachmentFile(model.FileAttachment1Upload, model.Id);
            }

            await _svc.UpdateAsync(model);

            // If condition changed to Questionable, send email to tooling manager
            if (model.Condition == ToolCondition.Questionable &&
                oldCondition != ToolCondition.Questionable)
            {
                NotifyToolingManagerQuestionable(model);
            }

            return Json(new { ok = true });
        }




        private bool IsAjax()
        {
            if (Request?.Headers is null) return false;
            if (!Request.Headers.TryGetValue("X-Requested-With", out var header)) return false;
            return string.Equals(header.ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        }
        // Controllers/ToolingInventoryController.cs


        private string? SaveToolAttachmentFile(IFormFile? file, int id)
        {
            if (file == null || file.Length == 0) return null;

            // Physical root from appsettings – change key if you want a different one
            var root = _config["Paths:ToolingInventoryUploads"]
                       ?? _config["Paths:ToolingWorkOrderUploads"]; // fallback

            if (string.IsNullOrWhiteSpace(root))
                throw new Exception("Paths:ToolingInventoryUploads (or ToolingWorkOrderUploads) not configured.");

            if (!Directory.Exists(root))
                Directory.CreateDirectory(root);

            var ext = Path.GetExtension(file.FileName); // keep original extension
            var fileName = $"fileAttachment1_{id}{ext}";   // <- this is your fileAttachment1_<id>
            var fullPath = Path.Combine(root, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                file.CopyTo(stream);
            }

            // Because root ends with ...\wwwroot\uploads, the web path is /uploads/<fileName>
            var webPath = $"/uploads/{fileName}";
            return webPath;
        }
        private void NotifyToolingManagerQuestionable(ToolItemModel m)
        {
            // Priority: OverrideAllTo > QuestionableToolingTo > WorkOrderRequestTo
            var overrideTo = _config["Email:OverrideAllTo"];
            var questionableTo = _config["Tooling:QuestionableToolingTo"];
            var fallback = _config["Tooling:WorkOrderRequestTo"];

            var to =
                !string.IsNullOrWhiteSpace(overrideTo) ? overrideTo :
                !string.IsNullOrWhiteSpace(questionableTo) ? questionableTo :
                fallback;

            if (string.IsNullOrWhiteSpace(to)) return;

            // Build an absolute URL with asm=<AssemblyNumber> so the search box is pre-filled
            var invUrl = Url.Action(
                action: "Index",
                controller: "ToolingInventory",
                values: new { asm = m.AssemblyNumber },
                protocol: Request?.Scheme ?? "https"
            );

            var subject =
                $"Tooling marked QUESTIONABLE – {m.AssemblyNumber} {m.ToolItem} {m.ToolNumber}";

            var body = $@"
Tooling has been marked as QUESTIONABLE.

Assembly: {m.AssemblyNumber}
Tool Item: {m.ToolItem}
Tool Number: {m.ToolNumber}
Location: {m.Location ?? "-"}
Condition: {m.Condition}
Status: {m.Status}

If the tooling is OK:
  • Please update the Condition to ReadyForProduction in the Tooling Inventory page.

If the tooling is NOT OK:
  • Please create a new Tool Work Order to fix the tooling.

To review this tool directly in Tooling Inventory (with the search box pre-filled for this assembly),
open this link:
{invUrl}

If a file attachment was added (image or video), it is attached to this email and can also be viewed
via the Preview button beside this tool in Tooling Inventory.

This email was generated automatically by the DashboardReportApp.
";

            // Try to resolve the attachment physical path (from /uploads/... to actual disk path)
            string? attachmentPath = null;

            if (!string.IsNullOrWhiteSpace(m.FileAttachment1))
            {
                // Physical root for /uploads from appsettings
                var root = _config["Paths:ToolingInventoryUploads"]
                           ?? _config["Paths:ToolingWorkOrderUploads"]; // fallback if needed

                if (!string.IsNullOrWhiteSpace(root))
                {
                    // m.FileAttachment1 is like "/uploads/fileAttachment1_123.jpg"
                    var webPath = m.FileAttachment1.Replace("\\", "/");

                    const string marker = "/uploads/";
                    var idx = webPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var fileName = webPath.Substring(idx + marker.Length); // "fileAttachment1_123.jpg"
                        var physical = Path.Combine(root, fileName);

                        if (System.IO.File.Exists(physical))
                        {
                            attachmentPath = physical;
                        }
                    }
                }
            }

            // Send with attachment (second attachment slot left empty)
            _shared.SendEmailWithAttachment(to, attachmentPath, null, subject, body);
        }


        [HttpGet]
        public async Task<IActionResult> Preview(int id)
        {
            var model = await _svc.GetByIdAsync(id);
            if (model is null) return NotFound("Tooling item not found.");
            return PartialView("_ToolItemPreview", model);
        }

    }
}
