using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Globalization;
using System.Linq;

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
Link to Calendar:
http://192.168.1.6:5000/Calendar
-------------------------------------------------------------------";

        _sharedService.SendEmailWithAttachment(
            "mhuff@sintergy.net",  // approver list
            null, null,
            $"Time‑Off Request: {model.LastName}, {model.FirstName}",
            body);

            _sharedService.SendEmailWithAttachment(
            "hr@sintergy.net",  // approver list
           null, null,
            $"Time‑Off Request: {model.LastName}, {model.FirstName}",
            body);


            /* ---------- User feedback ---------- */
            TempData["Success"] = "Record submitted and e-mail sent!";
            return RedirectToAction("Index");
    }






        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Approve(int id, string pin)
        {
            const string approvalPin = "1234";
            if (pin != approvalPin)
                return BadRequest("Invalid PIN");

            // 1️⃣  Mark the record approved
            using (var conn = new MySqlConnection(_mysql))
            {
                conn.Open();
                var cmd = new MySqlCommand(
                    "UPDATE servicerecords SET status='Approved' WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }

            // 2️⃣  Re-load all the details for the e-mail
            CalendarModel rec = _calendarService.GetServiceRecordById(id);
            List<string> dateList = new List<string>();
            string attribute = "";
            string explanation = "";
            DateTime? submittedOn = null;

            using (var conn = new MySqlConnection(_mysql))
            {
                conn.Open();

                // pull attribute, explanation, submitted_on
                using (var cmd = new MySqlCommand(
                    "SELECT attribute, explanation, submitted_on " +
                    "FROM servicerecords WHERE id=@id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using var rdr = cmd.ExecuteReader();
                    if (rdr.Read())
                    {
                        attribute = rdr["attribute"].ToString();
                        explanation = rdr["explanation"].ToString();
                        submittedOn = rdr.GetDateTime("submitted_on");
                    }
                }

                // pull each requested date
                using (var cmd = new MySqlCommand(
                    "SELECT requested_date FROM servicerecord_dates WHERE servicerecord_id=@id",
                    conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                        dateList.Add(rdr.GetDateTime("requested_date")
                                         .ToString("MM/dd/yyyy"));
                }
            }

            // 3️⃣  Build the full approval e-mail body
            string datesText = dateList.Any()
                ? string.Join(", ", dateList)
                : "(none)";

            string body = $@"
Time-off request has been *approved*.

Employee   : {rec.LastName}, {rec.FirstName}
Department : {rec.Department}
Shift      : {rec.Shift}
Schedule   : {rec.Schedule}
Type       : {rec.TimeOffType}
Attribute  : {(!string.IsNullOrWhiteSpace(attribute) ? attribute : "(none)")}
Dates      : {datesText}

Explanation:
{explanation}

Submitted on {submittedOn:MM/dd/yyyy h:mm tt}
Approved on  {DateTime.Now:MM/dd/yyyy h:mm tt}
Link to Calendar:
http://192.168.1.6:5000/Calendar
";


            _sharedService.SendEmailWithAttachment(
               "calendar@sintergy.net",                // to employee
               null,
               null,
               $"Time-Off Request Has Been Approved",
               body);

            return Ok();
        }


        [HttpGet]
        public IActionResult VerifyAdminPin(string pin)
        {
            //if (pin == "1234") return Ok();
            //return Unauthorized();
            return Ok();
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
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ScheduleEvent(string Title, string Location, string Description,
                                           string Date, string StartTime, string EndTime)
        {
            var model = new CalendarEventModel
            {
                Title = Title?.Trim(),
                Location = Location,
                Description = Description,
                Date = DateTime.Parse(Date),
                StartTime = TimeSpan.Parse(StartTime),
                EndTime = TimeSpan.Parse(EndTime)
            };

            _calendarService.SaveCalendarEvent(model);

            // ─── Email Notification ───
            string body = $@"
A new calendar event has been scheduled.

Title      : {model.Title}
Location   : {model.Location}
Date       : {model.Date:MM/dd/yyyy}
Start Time : {model.StartTime:hh\:mm}
End Time   : {model.EndTime:hh\:mm}

Description:
{model.Description ?? "(none)"}

Submitted on {DateTime.Now:g}
Link to Calendar:
http://192.168.1.6:5000/Calendar
-------------------------------------------------------------------";

            _sharedService.SendEmailWithAttachment(
                "calendar@sintergy.net",  // to
                null, null,
                $"Scheduled Event: {model.Title}",
                body);

            return Ok();
        }


        [HttpGet]
        public JsonResult GetEvents()
        {
            var list = new List<FcEventDto>();

            // ─── yellow / green time-off requests ───
            foreach (var r in _calendarService.GetServiceRecords())
            {
                foreach (var d in r.DatesRequested)
                {
                    list.Add(new FcEventDto
                    {
                        id = $"SR{r.Id}",
                        title = $"{r.LastName}, {r.FirstName} – {r.TimeOffType}",
                        start = d.ToString("yyyy-MM-dd"),
                        end = d.ToString("yyyy-MM-dd"),  // same day is OK for all-day
                        color = r.Status == "Approved" ? "#28a745" : "#ffc107",
                        allDay = true,
                        vacBalance = r.VacationBalance,
                        reqDays = r.DatesRequested.Count,
                        explanation = r.Explanation
                    });
                }
            }

            // ─── blue scheduled events ───
            foreach (var e in _calendarService.GetCalendarEvents())
            {
                var st = e.Date + e.StartTime;
                var et = e.Date + e.EndTime;

                list.Add(new FcEventDto
                {
                    id = $"EV{e.Id}",
                    title = $"{e.Title} ({e.Location}) {e.StartTime:hh\\:mm}-{e.EndTime:hh\\:mm}",
                    start = st.ToString("yyyy-MM-ddTHH:mm:ss"),
                    end = et.ToString("yyyy-MM-ddTHH:mm:ss"),
                    color = "#0d6efd",
                    allDay = false,
                    vacBalance = 0,
                    reqDays = 0
                });
            }

            return Json(list);           // one flat, well-typed array
        }


        // CalendarController.cs
        [HttpGet]  // get one event for editing
        public IActionResult GetCalendarEvent(int id) => Json(_calendarService.GetCalendarEventById(id));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditCalendarEvent(CalendarEventModel m)
        {
            _calendarService.UpdateCalendarEvent(m);
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteCalendarEvent(int id)
        {
            _calendarService.DeleteCalendarEvent(id);
            return Ok();
        }

    }

}
