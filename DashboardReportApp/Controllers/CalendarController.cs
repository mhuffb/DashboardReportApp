using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Globalization;

namespace DashboardReportApp.Controllers
{
    public class CalendarController : Controller
    {
        private readonly CalendarService _calendarService;
        private readonly SharedService _sharedService;
        private readonly string _mysql;
        public CalendarController(CalendarService calendarService,
                              SharedService sharedService,
                              IConfiguration cfg)
        {
            _calendarService = calendarService;
            _sharedService = sharedService;
            _mysql = cfg.GetConnectionString("MySQLConnection");
        }
        public IActionResult Index()
        {
            var employees = _calendarService.GetEmployees();
            return View(employees);
        }


[HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Submit(CalendarModel model, string DatesRequested)
    {
        /* ---------- Trim employee names ---------- */
        model.FirstName = model.FirstName?.Trim();
        model.LastName = model.LastName?.Trim();

        /* ---------- Robust multi‑date parse ---------- */
        var parsedDates = new List<DateTime>();
        if (!string.IsNullOrWhiteSpace(DatesRequested))
        {
            foreach (var tok in DatesRequested.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (DateTime.TryParse(tok.Trim(), out var dt))
                    parsedDates.Add(dt.Date);
        }
        model.DatesRequested = parsedDates;

        model.SubmittedOn = DateTime.Now;            // set timestamp

        /* ---------- Persist to database ---------- */
        _calendarService.SaveServiceRecord(model);

        /* ---------- Build full e‑mail body ---------- */
        string dateList = parsedDates.Any()
            ? string.Join(", ", parsedDates.Select(d => d.ToString("MM/dd/yyyy")))
            : "(none)";

        string attrLine = string.IsNullOrWhiteSpace(model.Attribute)
            ? "(none)"
            : model.Attribute;

        string body = $@"
A new time‑off request is waiting for approval.

Status     : Waiting
Employee   : {model.LastName}, {model.FirstName}
Department : {model.Department}
Shift      : {model.Shift}
Schedule   : {model.Schedule}
Type       : {model.TimeOffType}
Attribute  : {attrLine}
Dates      : {dateList}

Explanation:
{model.Explanation}

Submitted on {model.SubmittedOn:g}
-------------------------------------------------------------------";

        _sharedService.SendEmailWithAttachment(
            "mhuff@sintergy.net",  // approver list
            null, null,
            $"Time‑Off Request: {model.LastName}, {model.FirstName}",
            body);

        /* ---------- User feedback ---------- */
        TempData["Success"] = "Record submitted and e‑mail sent!";
        return RedirectToAction("Index");
    }




        [HttpGet]
        public JsonResult GetEvents()
        {
            var recs = _calendarService.GetServiceRecords();

            var ev = recs.SelectMany(r => r.DatesRequested.Select(d => new {
                id = r.Id,
                title = $"{r.LastName}, {r.FirstName} – {r.TimeOffType}",
                start = d.ToString("yyyy-MM-dd"),
                color = r.Status == "Approved" ? "#28a745" : "#ffc107",
                allDay = true,
                /* extra data for approve popup */
                vacBalance = r.VacationBalance,
                reqDays = r.DatesRequested.Count
            }));

            return Json(ev);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Approve(int id)
        {
            using var conn = new MySqlConnection(_mysql);
            conn.Open();
            var cmd = new MySqlCommand(
                "UPDATE servicerecords SET status='Approved' WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            TempData["Success"] = "Request approved.";
            return RedirectToAction("Index");
        }
        

    }

}
