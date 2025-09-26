using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using DashboardReportApp.Controllers.Attributes;
using Microsoft.Extensions.Configuration;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected]
    public class ToolingHistoryController : Controller
    {
        private readonly ToolingHistoryService _toolingHistoryService;
        private readonly IConfiguration _config;
        private readonly SharedService _shared; // <-- use your SMTP helper
        
    private readonly IWebHostEnvironment _env;

        private readonly ILogger<ToolingHistoryController> _logger;
        public ToolingHistoryController(
            ToolingHistoryService toolingHistoryService,
            IConfiguration config,
        ILogger<ToolingHistoryController> logger,
        IWebHostEnvironment env,
            SharedService shared)
        {
            _toolingHistoryService = toolingHistoryService;
            _config = config;
            _shared = shared;
            _env = env;
            _logger = logger;
        }

        public IActionResult Index()
        {
            ViewBag.ToolingHistories = _toolingHistoryService.GetAll();
            return View();
        }

        // ---------- Tooling History modal ----------
        public IActionResult EditToolingHistoryModal(int? id)
        {
            var peopleAll = new[] { "Emery, J", "Shuckers, C", "Klebecha, B" };
            ViewBag.PeopleAll = peopleAll;

            ToolingHistoryModel model;
            if (id.HasValue && id.Value > 0)
            {
                model = _toolingHistoryService.GetById(id.Value) ?? new ToolingHistoryModel();
            }
            else
            {
                model = new ToolingHistoryModel
                {
                    InitiatedBy = "Emery, J",
                    DateInitiated = DateTime.Today
                };
            }
            return PartialView("_ToolingHistoryEditModal", model);
        }
        // Controllers/ToolingHistoryController.cs (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(DashboardReportApp.Models.ToolingHistoryModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var keyed = ModelState
                        .Where(kvp => kvp.Value.Errors.Count > 0)
                        .ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Errors.Select(e =>
                                string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage
                            ).ToArray()
                        );

                    var flat = ModelState
                        .Where(kvp => kvp.Value.Errors.Count > 0)
                        .SelectMany(kvp => kvp.Value.Errors.Select(e =>
                            string.IsNullOrWhiteSpace(e.ErrorMessage)
                                ? $"{kvp.Key}: Invalid value."
                                : $"{kvp.Key}: {e.ErrorMessage}"
                        ))
                        .ToList();

                    _logger.LogWarning("ToolingHistory/Edit validation failed: {Errors}", string.Join(" | ", flat));
                    Response.StatusCode = 400;
                    return new JsonResult(new
                    {
                        ok = false,
                        message = "Validation failed.",
                        errors = keyed,
                        flatErrors = flat
                    });
                }

                var groupId = _toolingHistoryService.SaveToolingHistory(model);
                return Json(new { ok = true, groupId });
            }
            catch (Exception ex)
            {
                // Log everything
                _logger.LogError(ex, "ToolingHistory/Edit save failed for Id={Id}", model.Id);

                // Return rich details in Development, safer summary in other envs
                Response.StatusCode = 500;
                return new JsonResult(new
                {
                    ok = false,
                    message = "Save failed.",
                    error = ex.GetBaseException().Message,
                    errorDetails = _env.IsDevelopment() ? ex.ToString() : null
                });
            }
        }
        // ---------- Tool Items ----------
        public IActionResult ToolItemsModal(int groupID)
        {
            var vm = _toolingHistoryService.GetGroupDetails(groupID);
            ViewBag.PeopleRecvFit = new[] { "Shuckers, C", "Klebecha, B" };
            ViewBag.GroupID = groupID;
            return PartialView("_ToolItemsModal", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddToolItemAjax(GroupToolItem model)
        {
            if (model.GroupID <= 0)
                return BadRequest("GroupID required.");

            _toolingHistoryService.AddToolItem(model);

            var vm = _toolingHistoryService.GetGroupDetails(model.GroupID);
            ViewBag.PeopleRecvFit = new[] { "Shuckers, C", "Klebecha, B" };
            ViewBag.GroupID = model.GroupID;
            return PartialView("_ToolItemsModal", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveAllToolItemsAjax(GroupDetailsViewModel vm)
        {
            if (vm?.GroupID <= 0) return BadRequest("GroupID required.");

            // Bulk fields from Receive All modal
            var bulkDateStr = Request.Form["__bulkDateReceived"].FirstOrDefault();
            var bulkRecvBy = Request.Form["__bulkReceivedBy"].FirstOrDefault();
            var alsoFitRaw = Request.Form["__alsoFit"].FirstOrDefault(); // "on" if checked
            var alsoFit = string.Equals(alsoFitRaw, "on", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(alsoFitRaw, "true", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(bulkDateStr) || !string.IsNullOrWhiteSpace(bulkRecvBy) || alsoFit)
            {
                DateTime? bulkDate = null;
                if (DateTime.TryParse(bulkDateStr, out var d)) bulkDate = d;

                var current = _toolingHistoryService.GetGroupDetails(vm.GroupID);
                foreach (var t in current.ToolItems)
                {
                    if (bulkDate.HasValue) t.DateReceived = bulkDate;
                    if (!string.IsNullOrWhiteSpace(bulkRecvBy)) t.ReceivedBy = bulkRecvBy;

                    if (alsoFit)
                    {
                        if (bulkDate.HasValue) t.DateFitted = bulkDate;
                        if (!string.IsNullOrWhiteSpace(bulkRecvBy)) t.FittedBy = bulkRecvBy;
                    }
                }

                _toolingHistoryService.SaveAllToolItems(current);

                var refreshed = _toolingHistoryService.GetGroupDetails(vm.GroupID);
                ViewBag.PeopleRecvFit = new[] { "Shuckers, C", "Klebecha, B" };
                ViewBag.GroupID = vm.GroupID;
                return PartialView("_ToolItemsModal", refreshed);
            }

            // Normal per-row save-all path
            _toolingHistoryService.SaveAllToolItems(vm);

            var fresh = _toolingHistoryService.GetGroupDetails(vm.GroupID);
            ViewBag.PeopleRecvFit = new[] { "Shuckers, C", "Klebecha, B" };
            ViewBag.GroupID = vm.GroupID;
            return PartialView("_ToolItemsModal", fresh);
        }

        // ---------- Receive All / Fit All modals ----------
        public IActionResult ReceiveAllModal(int groupID)
        {
            ViewBag.GroupID = groupID;
            ViewBag.PeopleRecvFit = new[] { "Shuckers, C", "Klebecha, B" };
            return PartialView("_ReceiveAllModal");
        }

        public IActionResult FitAllModal(int groupID)
        {
            ViewBag.GroupID = groupID;
            ViewBag.PeopleRecvFit = new[] { "Shuckers, C", "Klebecha, B" };
            return PartialView("_FitAllModal");
        }

        // ---------- Request PO Email (uses SharedService SMTP) ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RequestPoNumber(int id)
        {
            var rec = _toolingHistoryService.GetById(id);
            if (rec == null)
            {
                Response.StatusCode = 404;
                return Json(new { ok = false, error = "Record not found." });
            }

            try
            {
                var to = _config["Tooling:PoRequestTo"] ?? "purchasing@sintergy.net";

                var subject = $"PO Request — Group {rec.GroupID} — Tool {rec.ToolNumber ?? "(n/a)"}";
                var body =
$@"Please issue a PO.

Group: {rec.GroupID}
Part: {rec.Part ?? "(n/a)"}
Tool #: {rec.ToolNumber ?? "(n/a)"}
Revision: {rec.Revision ?? "(n/a)"}
Reason: {rec.Reason ?? "(n/a)"}
Vendor: {rec.ToolVendor ?? "(n/a)"}
Initiated By: {rec.InitiatedBy ?? "(n/a)"}
Date Initiated: {(rec.DateInitiated?.ToString("yyyy-MM-dd") ?? "(n/a)")}
Date Due: {(rec.DateDue?.ToString("yyyy-MM-dd") ?? "(n/a)")}
Desc: {rec.ToolDesc ?? "(n/a)"}";

                // Use your helper (no attachments here)
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
    }
}
