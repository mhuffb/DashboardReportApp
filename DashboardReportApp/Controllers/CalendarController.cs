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
            TempData["Success"] = "Record submitted and e-mail sent!";
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
        public IActionResult Approve(int id, string pin)
        {
            const string approvalPin = "1234";   // TODO: move to config

            if (pin != approvalPin)
                return BadRequest("Invalid PIN");

            using var conn = new MySqlConnection(_mysql);
            conn.Open();

            var cmd = new MySqlCommand(
                "UPDATE servicerecords SET status='Approved' WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            // ─── Try e-mail, but don't fail approval if it blows up ───
            try
            {
                var rec = _calendarService.GetServiceRecordById(id);
                if (!string.IsNullOrWhiteSpace(rec?.Email))
                {
                    string body =
                        $"Time-off request for {rec.LastName}, {rec.FirstName} has been approved.";
                    _sharedService.SendEmailWithAttachment(
                        rec.Email, null, null,
                        "Your Time-Off Request Has Been Approved",
                        body);
                }
            }
            catch (Exception ex)
            {
                // log and continue
                Console.WriteLine($"E-mail failed: {ex.Message}");
            }

            return Ok();   // JS sees 200 → success
        }


        [HttpGet]
        public IActionResult VerifyAdminPin(string pin)
        {
            if (pin == "1234") return Ok();
            return Unauthorized();
        }

        [HttpGet]
        public JsonResult GetRecord(int id)
        {
            var rec = _calendarService.GetServiceRecordById(id);
            using var conn = new MySqlConnection(_mysql);
            conn.Open();
            var dates = new List<string>();

            var cmd = new MySqlCommand("SELECT requested_date FROM servicerecord_dates WHERE servicerecord_id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                dates.Add(Convert.ToDateTime(rdr["requested_date"]).ToString("MM/dd/yyyy"));

            return Json(new
            {
                timeOffType = rec.TimeOffType,
                shift = rec.Shift,
                explanation = rec.Explanation,
                dates = dates
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditRecord(int id, string type, string shift, string explanation, string dates)
        {
            using var conn = new MySqlConnection(_mysql);
            conn.Open();
            using var tran = conn.BeginTransaction();

            var update = new MySqlCommand(@"
        UPDATE servicerecords
        SET time_off_type = @type, shift = @shift, explanation = @expl
        WHERE id = @id", conn, tran);
            update.Parameters.AddWithValue("@type", type);
            update.Parameters.AddWithValue("@shift", shift);
            update.Parameters.AddWithValue("@expl", explanation);
            update.Parameters.AddWithValue("@id", id);
            update.ExecuteNonQuery();

            var del = new MySqlCommand("DELETE FROM servicerecord_dates WHERE servicerecord_id = @id", conn, tran);
            del.Parameters.AddWithValue("@id", id);
            del.ExecuteNonQuery();

            foreach (var dateStr in dates.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (DateTime.TryParse(dateStr.Trim(), out var d))
                {
                    var ins = new MySqlCommand("INSERT INTO servicerecord_dates (servicerecord_id, requested_date) VALUES (@id, @dt)", conn, tran);
                    ins.Parameters.AddWithValue("@id", id);
                    ins.Parameters.AddWithValue("@dt", d.Date);
                    ins.ExecuteNonQuery();
                }
            }

            tran.Commit();
            return Ok();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteRecord(int id)
        {
            using var conn = new MySqlConnection(_mysql);
            conn.Open();

            var deleteDates = new MySqlCommand("DELETE FROM servicerecord_dates WHERE servicerecord_id = @id", conn);
            deleteDates.Parameters.AddWithValue("@id", id);
            deleteDates.ExecuteNonQuery();

            var deleteMain = new MySqlCommand("DELETE FROM servicerecords WHERE id = @id", conn);
            deleteMain.Parameters.AddWithValue("@id", id);
            deleteMain.ExecuteNonQuery();

            return Ok();
        }

    }

}
