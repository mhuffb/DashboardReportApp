using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")]
    public class ToolingWorkOrderController : Controller
    {
        private readonly ToolingWorkOrderService _service;
        private readonly SharedService _shared;
        private readonly IConfiguration _cfg;

        public ToolingWorkOrderController(
            ToolingWorkOrderService service,
            SharedService shared,
            IConfiguration cfg)
        {
            _service = service;
            _shared = shared;
            _cfg = cfg;
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
                    DateDue = DateTime.Today,
                    GroupID = _service.GetNextGroupID(),
                    InitiatedBy = "Emery, J"
                };
            }
            else
            {
                model = _service.GetToolingWorkOrdersById(id.Value);
                if (model == null) return NotFound();
            }

            PopulateHeaderLists(model);
            return PartialView("_ToolingWorkOrderEditModal", model);
        }

        private bool IsAjax() =>
      string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveToolingWorkOrder(ToolingHistoryModel model)
        {
            if (!ModelState.IsValid)
            {
                Response.StatusCode = 400;
                PopulateHeaderLists(model);
                return IsAjax()
                    ? PartialView("_ToolingWorkOrderEditModal", model)
                    : View("EditToolingWorkOrder", model);
            }

            try
            {
                if (model.Id == 0) _service.AddToolingWorkOrder(model);
                else _service.UpdateToolingWorkOrder(model);

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
                return PartialView("_ToolItemsModal", vmBad);
            }

            _service.AddToolItem(model);
            var vm = new GroupDetailsViewModel
            {
                GroupID = model.GroupID,
                ToolItems = _service.GetToolItemsByGroupID(model.GroupID),
                NewToolItem = new ToolItemViewModel { GroupID = model.GroupID }
            };
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


    }
}
