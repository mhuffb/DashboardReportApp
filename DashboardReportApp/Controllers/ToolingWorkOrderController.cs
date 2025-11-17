using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")]
    public class ToolingWorkOrderController : Controller
    {
        private readonly ToolingWorkOrderService _service;
        private readonly SharedService _shared;
        private readonly IConfiguration _cfg;
        private readonly ToolingInventoryService _inv;

        public ToolingWorkOrderController(
            ToolingWorkOrderService service,
            SharedService shared,
            IConfiguration cfg,
            ToolingInventoryService inv)      // ← add
        {
            _service = service;
            _shared = shared;
            _cfg = cfg;
            _inv = inv;                       // ← add
        }

       

        public IActionResult Index()
        {
            var nextGroupId = _service.GetNextGroupID();
            ViewBag.NextGroupID = nextGroupId;

            var toolingWorkOrder = new ToolingHistoryModel
            {
                DateInitiated = DateTime.Today,
                DateDue = DateTime.Today,
                InitiatedBy = "Emery, J"
            };

            var toolingWorkOrders = _service.GetToolingWorkOrders();
            ViewBag.ToolingWorkOrders = toolingWorkOrders;

            ViewBag.ToolingAll = toolingWorkOrders;
            ViewBag.ToolingInProgress = toolingWorkOrders.Where(h => !h.DateReceived.HasValue).ToList();

            PopulateHeaderLists(toolingWorkOrder);
            return View(toolingWorkOrder);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(ToolingHistoryModel toolingWorkOrder)
        {
            if (ModelState.IsValid)
            {
                _service.AddToolingWorkOrder(toolingWorkOrder);
                TempData["Success"] = "Tooling Work Order created.";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.ToolingWorkOrders = _service.GetToolingWorkOrders();
            PopulateHeaderLists(toolingWorkOrder);
            return View(toolingWorkOrder);
        }

        [HttpGet]
        public IActionResult EditToolingWorkOrderModal(int? id)
        {
            ToolingHistoryModel model;
            if (id is null || id == 0)
            {
                model = new ToolingHistoryModel
                {
                    DateInitiated = DateTime.Today,
                    DateDue = null,
                    GroupID = _service.GetNextGroupID(),
                    InitiatedBy = "Emery, J"
                };
            }
            else
            {
                model = _service.GetToolingWorkOrdersById(id.Value);
                if (model == null) return NotFound();
            }
            // NEW: for Ref PO dropdown
            var toolingWorkOrders = _service.GetToolingWorkOrders();
            ViewBag.ToolingInProgress = toolingWorkOrders
                .Where(h => !h.DateReceived.HasValue)
                .ToList();
            //  populate lists AND ToolingInProgress for the modal
            PopulateHeaderLists(model);
            var all = _service.GetToolingWorkOrders();
            ViewBag.ToolingInProgress = all.Where(h => !h.DateReceived.HasValue).ToList();
            return PartialView("_ToolingWorkOrderEditModal", model);
        }

        private bool IsAjax() =>
      string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveToolingWorkOrder(
     ToolingHistoryModel model,
     IFormFile? attachment)
        {
            if (!ModelState.IsValid)
            {
                Response.StatusCode = 400;

                var toolingWorkOrders = _service.GetToolingWorkOrders();
                ViewBag.ToolingInProgress = toolingWorkOrders
                    .Where(h => !h.DateReceived.HasValue)
                    .ToList();

                PopulateHeaderLists(model);

                var all = _service.GetToolingWorkOrders();
                ViewBag.ToolingInProgress = all.Where(h => !h.DateReceived.HasValue).ToList();

                return IsAjax()
                    ? PartialView("_ToolingWorkOrderEditModal", model)
                    : View("EditToolingWorkOrder", model);
            }

            try
            {
                // 1) Insert or update header first
                if (model.Id == 0)
                {
                    _service.AddToolingWorkOrder(model);   // now sets model.Id
                }
                else
                {
                    _service.UpdateToolingWorkOrder(model);
                }

                // 2) If there is a file, save it and update header with filename
                if (attachment != null && attachment.Length > 0)
                {
                    var newFileName = SaveToolingAttachmentFile(model.Id, attachment);
                    model.AttachmentFileName = newFileName;

                    // persist filename
                    _service.UpdateToolingWorkOrder(model);
                }

                if (IsAjax())
                    return Json(new { ok = true, message = $"Work order for Group {model.GroupID} saved." });

                TempData["Success"] = $"Work order for Group {model.GroupID} saved.";
                return RedirectToAction("Index", "ToolingInventory");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                Response.StatusCode = 400;
                PopulateHeaderLists(model);

                var all = _service.GetToolingWorkOrders();
                ViewBag.ToolingInProgress = all.Where(h => !h.DateReceived.HasValue).ToList();

                return IsAjax()
                    ? PartialView("_ToolingWorkOrderEditModal", model)
                    : View("EditToolingWorkOrder", model);
            }
        }




        [HttpGet]
        public IActionResult ToolItemsModal(int groupID)
        {
            var groupRecords = _service.GetToolItemsByGroupID(groupID); // List<ToolItemViewModel>

            // get header to read the Part (assembly #)
            var header = _service.GetHeaderByGroupID(groupID); 

            var model = new GroupDetailsViewModel
            {
                GroupID = groupID,
                GroupName = header?.Part,                 // <-- put Part here
                ToolItems = groupRecords,
                NewToolItem = new ToolItemViewModel
                {
                    GroupID = groupID,
                    Quantity = 1   // <-- default qty
                }
            };
            PopulateItemLists(model.NewToolItem.ToolItem);
            return PartialView("_ToolItemsModal", model);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddToolItemAjax(ToolItemViewModel model)
        {
            if (!ModelState.IsValid)
            {
                Response.StatusCode = 400;
                var vmBad = new GroupDetailsViewModel
                {
                    GroupID = model.GroupID,
                    ToolItems = _service.GetToolItemsByGroupID(model.GroupID),
                    NewToolItem = model
                };
                PopulateItemLists(model.ToolItem);
                return PartialView("_ToolItemsModal", vmBad);
            }

            _service.AddToolItem(model);
            var vm = new GroupDetailsViewModel
            {
                GroupID = model.GroupID,
                ToolItems = _service.GetToolItemsByGroupID(model.GroupID),
                NewToolItem = new ToolItemViewModel { GroupID = model.GroupID }
            };
            PopulateItemLists();
            return PartialView("_ToolItemsModal", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveAllToolItemsAjax(GroupDetailsViewModel model)
        {
            if (model == null || model.GroupID <= 0)
            {
                Response.StatusCode = 400;
                return IsAjax() ? Content("Invalid group.") : RedirectToAction("Index", "ToolingInventory");
            }

            if (model.ToolItems != null)
            {
                foreach (var item in model.ToolItems)
                {
                    if (item.Id > 0)
                    {
                        var vm = new ToolItemViewModel
                        {
                            Id = item.Id,
                            GroupID = item.GroupID,
                            Action = item.Action,
                            ToolItem = item.ToolItem,
                            ToolNumber = item.ToolNumber,
                            ToolDesc = item.ToolDesc,
                            Revision = item.Revision,
                            Quantity = item.Quantity,
                            Cost = item.Cost,
                            ToolWorkHours = item.ToolWorkHours
                        };
                        _service.UpdateToolItem(vm);
                    }
                }
            }

            // AJAX => return refreshed modal body; Non-AJAX => redirect
            if (IsAjax())
            {
                var refreshed = new GroupDetailsViewModel
                {
                    GroupID = model.GroupID,
                    GroupName = _service.GetHeaderByGroupID(model.GroupID)?.Part,
                    ToolItems = _service.GetToolItemsByGroupID(model.GroupID),
                    NewToolItem = new ToolItemViewModel { GroupID = model.GroupID, Quantity = 1 }
                };
                PopulateItemLists();
                return PartialView("_ToolItemsModal", refreshed);
            }

            TempData["Success"] = "Tool items saved.";
            return RedirectToAction("Index", "ToolingInventory");
        }




        // --------- PO request (no textbox; uses config default) ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RequestPoNumber(int id)
        {
            try
            {
                var record = _service.GetToolingWorkOrdersById(id);
                if (record == null) return NotFound();

                // ⬇️ Put it here
                var items = _service.GetToolItemsByGroupID(record.GroupID); // now reads tooling_WorkOrder_item

                var subject = $"PO Request: Group {record.GroupID} / {record.Part}";
                var link = Url.Action("Index", "ToolingWorkOrder", null, Request.Scheme);
                var body = $@"
PO Request (Tooling Work Order)
----------------------------
GroupID: {record.GroupID}
Reason: {record.Reason}
Vendor: {record.ToolVendor}
Part: {record.Part}
Due: {record.DateDue:MM-dd-yyyy}
Estimated Cost: {(record.Cost?.ToString("C", CultureInfo.GetCultureInfo("en-US")) ?? "n/a")}
Items:
{string.Join("\n",
    items.Select(it =>
        $"- {it.Action} | {it.ToolItem} | {it.ToolNumber} | {it.ToolDesc} | Qty={it.Quantity} | " +
        $"Cost={(it.Cost?.ToString("C", CultureInfo.GetCultureInfo("en-US")) ?? "n/a")}"
    )
)}
Open in Dashboard: {link}
";


                var to = string.IsNullOrWhiteSpace(_cfg["Tooling:PoRequestTo"])
                    ? "tooling@sintergy.local"
                    : _cfg["Tooling:PoRequestTo"];

                _shared.SendEmailWithAttachment(
                    receiverEmail: to,
                    attachmentPath: null,
                    attachmentPath2: null,
                    subject: subject,
                    body: body
                );
                _service.MarkPoRequested(id);

                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { ok = false, error = ex.Message });
            }
        }


        // GET /ToolingWorkOrder/GeneratePackingSlip?groupID=123
        [HttpGet]
        public IActionResult GeneratePackingSlip(int groupID, bool email = true)
        {
            var saveFolder = _cfg["Tooling:SaveFolder"];
            if (string.IsNullOrWhiteSpace(saveFolder))
                return Problem("Tooling:SaveFolder not configured.");

            var path = _service.SavePackingSlipPdf(groupID, saveFolder);

            PrintPackingSlip(path);

            if (email)
            {
                var to = _cfg["Tooling:PackingSlipEmailTo"];
                if (!string.IsNullOrWhiteSpace(to))
                {
                    var header = _service.GetHeaderByGroupID(groupID);
                    _shared.SendEmailWithAttachment(
                        receiverEmail: to,
                        attachmentPath: path,
                        attachmentPath2: null,
                        subject: $"Packing Slip – Group {header?.GroupID} – {header?.Part}",
                        body: $"Attached is the packing slip for Group {header?.GroupID} ({header?.Part})."
                    );
                }
            }

            var fileName = System.IO.Path.GetFileName(path);
            return PhysicalFile(path, "application/pdf", fileName, enableRangeProcessing: true);
        }
        [HttpGet]
        public IActionResult ItemsTable(int groupID)
        {
            var items = _service.GetToolItemsByGroupID(groupID) ?? new List<ToolItemViewModel>();
            return PartialView("_ToolItemsInlineTable", items);
        }
        private void PopulateHeaderLists(ToolingHistoryModel? current = null)
        {
            // Sensible defaults (front-load the common ones)
            var defaultReasons = new List<string> { "New Customer Purchase (5030)", "Repair at Sintergy Cost (5045)", "Breakage Due to Negligence (5040)", "Fitting/Setting" };
            var defaultVendors = new List<string> { "J.I.T. Tool & Die", "Gerg Tool & Die Inc.", "Quala Die", "Elk County Tool & Die", "Internal Toolroom" };
            var defaultPeople = new List<string> { "Emery, J", "Shuckers, C", "Klebecha, B" };

            // Pull distincts from existing data
            var dbReasons = _service.GetDistinctReasons();
            var dbVendors = _service.GetDistinctVendors();
            var dbPeople = _service.GetDistinctInitiators();

            // Merge & de-dup while preserving order (defaults first)
            static List<string> Merge(List<string> defaults, List<string> db, string? ensure)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var res = new List<string>();

                foreach (var s in defaults)
                    if (!string.IsNullOrWhiteSpace(s) && set.Add(s)) res.Add(s);

                foreach (var s in db)
                    if (!string.IsNullOrWhiteSpace(s) && set.Add(s)) res.Add(s);

                if (!string.IsNullOrWhiteSpace(ensure) && set.Add(ensure)) res.Add(ensure);

                return res;
            }

            var reasons = Merge(defaultReasons, dbReasons, current?.Reason);
            var vendors = Merge(defaultVendors, dbVendors, current?.ToolVendor);
            var people = Merge(defaultPeople, dbPeople, current?.InitiatedBy);

            ViewBag.ReasonList = reasons;
            ViewBag.VendorList = vendors;
            ViewBag.InitiatedList = people;
        }
        // Open the Edit Tool Work Order modal by GroupID
        // ToolingWorkOrderController.cs

        [HttpGet]
        public IActionResult EditToolingWorkOrderModalByGroup(int groupID)
        {
            // Find a WorkOrder row for this group (pick most recent if multiple)
            var hx = _service.GetToolingWorkOrders()
                         .Where(h => h.GroupID == groupID)
                         .OrderByDescending(h => h.DateInitiated)
                         .FirstOrDefault();

            if (hx == null)
                return NotFound($"No tooling Work Order found for group {groupID}.");

            // Reuse your existing modal action that expects a WorkOrder Id
            // IMPORTANT: call the action method directly to render the same partial
            return EditToolingWorkOrderModal(hx.Id);
        }

        [HttpGet]
        public IActionResult ToolItemsModalByGroup(int groupID)
        {
            // Reuse your existing modal action that already takes groupID
            return ToolItemsModal(groupID);
        }

        // ToolingWorkOrderController.cs
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteToolItemAjax(int id, int groupID)
        {
            try
            {
                _service.DeleteToolItem(id);

                // Re-render just the items table for this group
                var items = _service.GetToolItemsByGroupID(groupID) ?? new List<ToolItemViewModel>();
                return PartialView("_ToolItemsInlineTable", items);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Content("Delete failed: " + ex.Message);
            }
        }
        // ToolingWorkOrderController.cs

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RequestWorkOrder(int id)
        {
            try
            {
                var record = _service.GetToolingWorkOrdersById(id);
                if (record == null) return NotFound();

                var items = _service.GetToolItemsByGroupID(record.GroupID);

                var subject = $"Work Order Request: Group {record.GroupID} / {record.Part}";
                var link = Url.Action("Index", "ToolingWorkOrder", null, Request.Scheme);

                var body = $@"
WORK ORDER REQUEST (Internal Toolroom)
-------------------------------------
GroupID: {record.GroupID}
Part: {record.Part}
Reason: {record.Reason}
Requested By: {record.InitiatedBy}
Due: {record.DateDue:MM-dd-yyyy}

Requested Work Items:
{string.Join("\n", items.Select(it =>
            $"- {it.Action} | {it.ToolItem} | {it.ToolNumber} | {it.ToolDesc} | Qty={it.Quantity} | " +
            $"Est.Hours={(it.ToolWorkHours?.ToString() ?? "n/a")}"
        ))}

Open in Dashboard: {link}
".Trim();

                var to = string.IsNullOrWhiteSpace(_cfg["Tooling:WorkOrderRequestTo"])
                    ? "toolroom@sintergy.local"
                    : _cfg["Tooling:WorkOrderRequestTo"];

                _shared.SendEmailWithAttachment(
                    receiverEmail: to,
                    attachmentPath: null,
                    attachmentPath2: null,
                    subject: subject,
                    body: body
                );

                // reuse existing flag so UI can show "requested"
                _service.MarkPoRequested(id);

                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { ok = false, error = ex.Message });
            }
        }

        // Controllers/ToolingWorkOrderController.cs  (additions)

        [HttpGet]
        public IActionResult CompleteWorkOrderModal(int groupID)
        {
            var header = _service.GetHeaderByGroupID(groupID);
            if (header == null) return NotFound($"No work order found for group {groupID}.");

            

            var defaults = new List<string> { "Emery, J", "Shuckers, C", "Klebecha, B" }; // ← your defaults
            var db = _service.GetDistinctReceivers();

            var merged = Merge(defaults, db, header?.Received_CompletedBy);
            ViewBag.ReceivedByList = merged;

            var vm = new CompleteWorkOrderVM
            {
                GroupID = groupID,
                DateReceived = header?.DateReceived ?? DateTime.Today,
                Received_CompletedBy = header?.Received_CompletedBy ?? string.Empty
            };
            ModelState.Clear();  // <- IMPORTANT: let the model’s default render
            return PartialView("_CompleteWorkOrderModal", vm);
        }
        static List<string> Merge(IEnumerable<string> defaults, IEnumerable<string> db, string? ensure)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            foreach (var s in defaults.Where(s => !string.IsNullOrWhiteSpace(s)))
                if (set.Add(s)) result.Add(s);

            foreach (var s in db.Where(s => !string.IsNullOrWhiteSpace(s)))
                if (set.Add(s)) result.Add(s);

            if (!string.IsNullOrWhiteSpace(ensure) && set.Add(ensure!))
                result.Add(ensure!);

            return result;
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteWorkOrder(CompleteWorkOrderVM vm)
        {
            if (!ModelState.IsValid)
            {
                Response.StatusCode = 400;
                ViewBag.ReceivedByList = _service.GetDistinctReceivers();
                return PartialView("_CompleteWorkOrderModal", vm);
            }

            var header = _service.GetHeaderByGroupID(vm.GroupID);
            if (header == null)
            {
                Response.StatusCode = 404;
                return Json(new { ok = false, error = $"No header for group {vm.GroupID}." });
            }

            var items = _service.GetToolItemsByGroupID(vm.GroupID) ?? new List<ToolItemViewModel>();
            var makeNew = 0; var makeNewExists = 0;
            var markedUnavailable = 0;
            var details = new List<string>();

            // normalize helpers
            static string N(string? s) => (s ?? string.Empty).Trim();
            var asm = N(header.Part);                         // Assembly #
            var dateInit = header.DateInitiated;              // Date Initiated
            DateTime? eta = header.DateDue;                   // Date Due (ETA)

            var unavailableActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Tinalox Coat", "Metalife Coat", "Reface", "Fitting" };

            foreach (var it in items)
            {
                var action = N(it.Action);
                var toolItem = N(it.ToolItem);
                var toolNum = N(it.ToolNumber);

                if (string.IsNullOrWhiteSpace(toolItem) || string.IsNullOrWhiteSpace(toolNum) || string.IsNullOrWhiteSpace(asm))
                    continue; // skip incomplete keys

                // --- MAKE NEW: Add to inventory if missing ---
                if (action.Equals("Make New", StringComparison.OrdinalIgnoreCase))
                {
                    var exists = await _inv.FindByKeyAsync(asm, toolItem, toolNum);
                    if (exists == null)
                    {
                        var newId = await _inv.CreateBasicAsync(asm, toolItem, toolNum);
                        makeNew++;
                        details.Add($"Added to inventory (Id {newId}): {asm} | {toolItem} | {toolNum}");
                    }
                    else
                    {
                        makeNewExists++;
                        details.Add($"Already in inventory: {asm} | {toolItem} | {toolNum}");
                    }
                    continue;
                }

                // --- COAT/REPAIR/FITTING: mark unavailable ---
                if (unavailableActions.Contains(action))
                {
                    await _inv.MarkUnavailableAsync(
                        asm, toolItem, toolNum,
                        reason: action,
                        dateUnavailable: dateInit,
                        eta: eta);
                    markedUnavailable++;
                    details.Add($"Marked unavailable ({action}): {asm} | {toolItem} | {toolNum}");
                }
            }

            // mark the WO complete
            _service.MarkWorkOrderCompleteByGroup(vm.GroupID, vm.DateReceived, vm.Received_CompletedBy);

            // Build a compact summary for SweetAlert
            var parts = new List<string>();
            if (makeNew > 0) parts.Add($"<li><strong>Added to inventory</strong>: {makeNew}</li>");
            if (makeNewExists > 0) parts.Add($"<li><strong>Already in inventory</strong>: {makeNewExists}</li>");
            if (markedUnavailable > 0) parts.Add($"<li><strong>Marked unavailable</strong>: {markedUnavailable}</li>");

            var html = parts.Count > 0
                ? $"<ul class='mb-0'>{string.Join("", parts)}</ul>"
                : "<span>No inventory changes recorded.</span>";

            // Optional: keep a tiny preview instead of the full list
            // var preview = details.Take(5).ToList();
            // if (details.Count > 5) preview.Add($"…and {details.Count - 5} more.");
            // var html = $"<ul class='mb-0'>{string.Join("", preview.Select(p => $"<li>{System.Net.WebUtility.HtmlEncode(p)}</li>"))}</ul>";

            return Json(new
            {
                ok = true,
                title = $"Completed Group {vm.GroupID}",
                html,
                // message kept for logging/debug if you want:
                // message = $"Completed Tool Work Order {vm.GroupID}..."
            });

        }

        // ToolingWorkOrderController
        private void PopulateItemLists(string? ensureToolItem = null)
        {
            var db = _service.GetDistinctToolItemNames();
            // merge + dedupe with the current value so it appears even if not in DB yet
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var merged = new List<string>();
            foreach (var s in db) if (!string.IsNullOrWhiteSpace(s) && set.Add(s)) merged.Add(s);
            if (!string.IsNullOrWhiteSpace(ensureToolItem) && set.Add(ensureToolItem)) merged.Add(ensureToolItem);
            ViewBag.ToolItemList = merged;
        }
        private void PrintPackingSlip(string pdfPath)
        {
            var exePath = _cfg["Printing:SumatraExePath"];
            var printer = _cfg["Printers:ToolingPackingSlip"];

            if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(printer))
                return; // silently skip if not configured

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"-print-to \"{printer}\" -silent \"{pdfPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                Process.Start(psi);
            }
            catch
            {
                // optional: log if you have logging wired up
            }
        }


        [HttpGet]
        public IActionResult AttachmentPreviewModal(int groupID)
        {
            var header = _service.GetHeaderByGroupID(groupID);
            if (header == null)
                return NotFound($"No work order found for group {groupID}.");

            var vm = new ToolingAttachmentPreviewVM
            {
                GroupID = groupID
            };

            if (string.IsNullOrWhiteSpace(header.AttachmentFileName))
            {
                vm.FileUrl = null;
                vm.FileName = null;
            }
            else
            {
                // MUST match the physical path: wwwroot\uploads\tooling\...
                vm.FileName = header.AttachmentFileName;
                vm.FileUrl = $"/uploads/{header.AttachmentFileName}";
            }

            return PartialView("_ToolingAttachmentModal", vm);
        }

        private static string SanitizeFileNameLocal(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            foreach (var bad in Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');

            return name;
        }

        private string? SaveToolingAttachmentFile(int headerId, IFormFile file)
        {
            if (file == null || file.Length == 0) return null;

            var root = _cfg["Paths:ToolingWorkOrderUploads"];
            if (string.IsNullOrWhiteSpace(root))
                throw new Exception("Paths:ToolingWorkOrderUploads not configured.");

            Directory.CreateDirectory(root);

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".bin";

            var fileName = $"toolworkorderFile1_{headerId}{ext}";
            fileName = SanitizeFileNameLocal(fileName);

            var fullPath = Path.Combine(root, fileName);

            using (var fs = new FileStream(fullPath, FileMode.Create))
            {
                file.CopyTo(fs);
            }

            return fileName;
        }


    }
}
