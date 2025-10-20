using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using iText.Layout.Element;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq; // for string.Join / Select in RequestPoNumber

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")]
    public class ToolingHistoryController : Controller
    {
        private readonly ToolingHistoryService _service;
        private readonly SharedService _shared;
        private readonly IConfiguration _cfg;

        public ToolingHistoryController(
            ToolingHistoryService service,
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

            var toolingHistory = new ToolingHistoryModel
            {
                DateInitiated = DateTime.Today,
                DateDue = DateTime.Today,
                InitiatedBy = "Emery, J"
            };

            var toolingHistories = _service.GetToolingHistories();
            ViewBag.ToolingHistories = toolingHistories;

            ViewBag.ToolingAll = toolingHistories;
            ViewBag.ToolingInProgress = toolingHistories.Where(h => !h.DateReceived.HasValue).ToList();

            PopulateHeaderLists(toolingHistory);
            return View(toolingHistory);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(ToolingHistoryModel toolingHistory)
        {
            if (ModelState.IsValid)
            {
                _service.AddToolingHistory(toolingHistory);
                TempData["Success"] = "Tooling history created.";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.ToolingHistories = _service.GetToolingHistories();
            PopulateHeaderLists(toolingHistory);
            return View(toolingHistory);
        }

        [HttpGet]
        public IActionResult EditToolingHistoryModal(int? id)
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
                model = _service.GetToolingHistoryById(id.Value);
                if (model == null) return NotFound();
            }

            PopulateHeaderLists(model);
            return PartialView("_ToolingHistoryEditModal", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveToolingHistory(ToolingHistoryModel model)
        {
            if (!ModelState.IsValid)
            {
                Response.StatusCode = 400;
                PopulateHeaderLists(model);
                return PartialView("_ToolingHistoryEditModal", model);
            }

            try
            {
                if (model.Id == 0) _service.AddToolingHistory(model);
                else _service.UpdateToolingHistory(model);

                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                Response.StatusCode = 400;
                PopulateHeaderLists(model);
                return PartialView("_ToolingHistoryEditModal", model);
            }
        }



        [HttpGet]
        public IActionResult ToolItemsModal(int groupID)
        {
            var groupRecords = _service.GetToolItemsByGroupID(groupID); // List<ToolItemViewModel>

            // get header to read the Part (assembly #)
            var header = _service.GetHeaderByGroupID(groupID); // returns ToolingHistoryModel

            var model = new GroupDetailsViewModel
            {
                GroupID = groupID,
                GroupName = header?.Part,                 // <-- put Part here
                ToolItems = groupRecords,
                NewToolItem = new ToolItemViewModel { GroupID = groupID }
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

        // NEW: Save all tool items for the group in one go
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveAllToolItemsAjax(GroupDetailsViewModel model)
        {
            if (model == null || model.GroupID <= 0)
            {
                Response.StatusCode = 400;
                return Content("Invalid group.");
            }

            if (model.ToolItems != null)
            {
                foreach (var item in model.ToolItems)
                {
                    if (item.Id > 0)
                    {
                        // Map GroupToolItem -> ToolItemViewModel for update (if your service expects VM)
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

            var refreshed = new GroupDetailsViewModel
            {
                GroupID = model.GroupID,
                ToolItems = _service.GetToolItemsByGroupID(model.GroupID),
                NewToolItem = new ToolItemViewModel { GroupID = model.GroupID }
            };
            return PartialView("_ToolItemsModal", refreshed);
        }



        // --------- PO request (no textbox; uses config default) ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RequestPoNumber(int id)
        {
            try
            {
                var record = _service.GetToolingHistoryById(id);
                if (record == null) return NotFound();

                // ⬇️ Put it here
                var items = _service.GetToolItemsByGroupID(record.GroupID); // now reads tooling_history_item

                var subject = $"PO Request: Group {record.GroupID} / {record.Part}";
                var link = Url.Action("Index", "ToolingHistory", null, Request.Scheme);
                var body = $@"
PO Request (Tooling History)
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
       
      
        // GET /ToolingHistory/GeneratePackingSlip?groupID=123
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
        // Open the Edit Tool History modal by GroupID
        // ToolingHistoryController.cs

        [HttpGet]
        public IActionResult EditToolingHistoryModalByGroup(int groupID)
        {
            // Find a history row for this group (pick most recent if multiple)
            var hx = _service.GetToolingHistories()
                         .Where(h => h.GroupID == groupID)
                         .OrderByDescending(h => h.DateInitiated)
                         .FirstOrDefault();

            if (hx == null)
                return NotFound($"No tooling history found for group {groupID}.");

            // Reuse your existing modal action that expects a history Id
            // IMPORTANT: call the action method directly to render the same partial
            return EditToolingHistoryModal(hx.Id);
        }

        [HttpGet]
        public IActionResult ToolItemsModalByGroup(int groupID)
        {
            // Reuse your existing modal action that already takes groupID
            return ToolItemsModal(groupID);
        }



    }
}
