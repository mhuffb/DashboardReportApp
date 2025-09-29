using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
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

            // names lists
            ViewBag.PeopleAll = new List<string> { "Emery, J", "Shuckers, C", "Klebecha, B" };
            ViewBag.PeopleRecvFit = new List<string> { "Shuckers, C", "Klebecha, B" };

            var toolingHistory = new ToolingHistoryModel
            {
                DateInitiated = DateTime.Today,
                DateDue = DateTime.Today,
                InitiatedBy = "Emery, J" // default as requested
            };

            var toolingHistories = _service.GetToolingHistories();
            ViewBag.ToolingHistories = toolingHistories;
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

            var toolingHistories = _service.GetToolingHistories();
            ViewBag.ToolingHistories = toolingHistories;
            return View(toolingHistory);
        }

        // --------- Edit/Create Tooling History (modal) ----------
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
                    GroupID = _service.GetNextGroupID()
                };
            }
            else
            {
                model = _service.GetToolingHistoryById(id.Value);
                if (model == null) return NotFound();
            }
            return PartialView("_ToolingHistoryEditModal", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveToolingHistory(ToolingHistoryModel model)
        {
            if (!ModelState.IsValid)
            {
                Response.StatusCode = 400;
                return PartialView("_ToolingHistoryEditModal", model);
            }

            try
            {
                if (model.Id == 0)
                    _service.AddToolingHistory(model);
                else
                    _service.UpdateToolingHistory(model);

                return Json(new { ok = true });
            }
            catch (MySqlException ex) when (ex.Number == 1062) // duplicate key
            {
                ModelState.AddModelError("", "That Group ID already exists. A new Group ID will be assigned — please try saving again.");
                Response.StatusCode = 400;
                return PartialView("_ToolingHistoryEditModal", model);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                Response.StatusCode = 400;
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
                            ToolWorkHours = item.ToolWorkHours,
                            DateDue = item.DateDue,
                            DateFitted = item.DateFitted,
                            DateReceived = item.DateReceived,
                            ReceivedBy = item.ReceivedBy,
                            FittedBy = item.FittedBy
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

        [HttpGet]
        public IActionResult ReceiveAllModal(int groupID)
        {
            return PartialView("_ReceiveAllModal", groupID);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReceiveAll(int groupID, string receivedBy, bool alsoFit)
        {
            Console.WriteLine($"[ReceiveAll] groupID={groupID}, receivedBy='{receivedBy}', alsoFit={alsoFit}");

            try
            {
                _service.ReceiveAllInGroup(groupID, receivedBy, DateTime.Now, alsoFit);
                var model = new GroupDetailsViewModel
                {
                    GroupID = groupID,
                    ToolItems = _service.GetToolItemsByGroupID(groupID),
                    NewToolItem = new ToolItemViewModel { GroupID = groupID }
                };
                return PartialView("_ToolItemsModal", model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReceiveAll][ERR] {ex.Message}");
                Response.StatusCode = 500;
                return Content("Error: " + ex.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // <-- was "lidateAntiForgeryToken" before
        public IActionResult FitAll(int groupID, string fittedBy)
        {
            if (string.IsNullOrWhiteSpace(fittedBy))
            {
                Response.StatusCode = 400;
                return Content("Fitted By is required.");
            }

            _service.FitAllInGroup(groupID, fittedBy.Trim(), DateTime.Today);

            var vm = new GroupDetailsViewModel
            {
                GroupID = groupID,
                ToolItems = _service.GetToolItemsByGroupID(groupID),
                NewToolItem = new ToolItemViewModel { GroupID = groupID }
            };
            return PartialView("_ToolItemsModal", vm);
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

                var subject = $"PO Request: Group {record.GroupID} / {record.Part} / {record.ToolNumber}";
                var link = Url.Action("Index", "ToolingHistory", null, Request.Scheme);
                var body = $@"
PO Request (Tooling History)
----------------------------
GroupID: {record.GroupID}
Reason: {record.Reason}
Vendor: {record.ToolVendor}
Part: {record.Part}
Tool#: {record.ToolNumber}
Revision: {record.Revision}
Due: {record.DateDue:yyyy-MM-dd}
Estimated Cost: {(record.Cost?.ToString("C") ?? "n/a")}
Hours: {(record.ToolWorkHours?.ToString() ?? "n/a")}
Desc: {record.ToolDesc}

Items:
{string.Join("\n", items.Select(it => $"- {it.Action} | {it.ToolItem} | {it.ToolNumber} | {it.ToolDesc} | Qty={(it.Quantity)} | Cost={(it.Cost?.ToString("C") ?? "n/a")}"))}

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

                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { ok = false, error = ex.Message });
            }
        }
        [HttpGet]
        public IActionResult FitAllModal(int groupID)
        {
            // Reuse the same simple modal pattern as ReceiveAll
            return PartialView("_FitAllModal", groupID);
        }

    }
}
