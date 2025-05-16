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

        private readonly ILogger<CalendarController> _logger;   // ← field
        public CalendarController(CalendarService calendarService,
                              SharedService sharedService,
                              IConfiguration cfg,
                              ILogger<CalendarController> logger)
        {
            _calendarService = calendarService;
            _sharedService = sharedService;

            _logger = logger;      // ← store
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
            "mhuff@sintergy.net",  // to mhuff
            null, null,
            $"Time‑Off Request: {model.LastName}, {model.FirstName}",
            body);

            _sharedService.SendEmailWithAttachment(
            "hr@sintergy.net",  // to hr
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
            const string approvalPin = "9412";
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
               "calendar@sintergy.net",                // to calendar
               null,
               null,
               $"Time-Off Request Has Been Approved",
               body);

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
            // 1️⃣  Load original record for comparison or details
            var original = _calendarService.GetServiceRecordById(id);
            var originalDates = _calendarService.GetRequestedDates(id);

            // 2️⃣  Apply updates in a transaction
            using var conn = new MySqlConnection(_mysql);
            conn.Open();
            using var tran = conn.BeginTransaction();

            var update = new MySqlCommand(@"
            UPDATE servicerecords
               SET time_off_type = @type,
                   shift = @shift,
                   explanation = @expl
             WHERE id = @id", conn, tran);
            update.Parameters.AddWithValue("@type", type);
            update.Parameters.AddWithValue("@shift", shift);
            update.Parameters.AddWithValue("@expl", explanation);
            update.Parameters.AddWithValue("@id", id);
            update.ExecuteNonQuery();

            var del = new MySqlCommand("DELETE FROM servicerecord_dates WHERE servicerecord_id = @id", conn, tran);
            del.Parameters.AddWithValue("@id", id);
            del.ExecuteNonQuery();

            var newDates = new List<DateTime>();
            foreach (var dateStr in dates.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (DateTime.TryParse(dateStr.Trim(), out var d))
                {
                    newDates.Add(d.Date);
                    var ins = new MySqlCommand(@"
                    INSERT INTO servicerecord_dates (servicerecord_id, requested_date)
                    VALUES (@id, @dt)", conn, tran);
                    ins.Parameters.AddWithValue("@id", id);
                    ins.Parameters.AddWithValue("@dt", d.Date);
                    ins.ExecuteNonQuery();
                }
            }

            tran.Commit();

            // 3️⃣  Build and send notification e-mail
            string origDatesText = originalDates.Any()
                ? string.Join(", ", originalDates.Select(d => d.ToString("MM/dd/yyyy")))
                : "(none)";
            string newDatesText = newDates.Any()
                ? string.Join(", ", newDates.Select(d => d.ToString("MM/dd/yyyy")))
                : "(none)";

            string body = $@"
A time-off request has been *edited*.

Employee     : {original.LastName}, {original.FirstName}

--- BEFORE ---
Type         : {original.TimeOffType}
Shift        : {original.Shift}
Dates        : {origDatesText}
Explanation  : {original.Explanation}

--- AFTER ---
Type         : {type}
Shift        : {shift}
Dates        : {newDatesText}
Explanation  : {explanation}

Edited on    : {DateTime.Now:MM/dd/yyyy h:mm tt}
Link to Calendar:
http://192.168.1.6:5000/Calendar
-------------------------------------------------------------------";

            _sharedService.SendEmailWithAttachment(
                "calendar@sintergy.net",    // to calendar
                null, null,
                $"Edited Time-Off Request: {original.LastName}, {original.FirstName}",
                body);

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteRecord(int id)
        {
            // 1️⃣  Load record details before deletion
            var rec = _calendarService.GetServiceRecordById(id);
            var dates = _calendarService.GetRequestedDates(id);
            string dateList = dates.Any()
                ? string.Join(", ", dates.Select(d => d.ToString("MM/dd/yyyy")))
                : "(none)";

            // 2️⃣  Delete from DB
            using var conn = new MySqlConnection(_mysql);
            conn.Open();

            var deleteDates = new MySqlCommand(
                "DELETE FROM servicerecord_dates WHERE servicerecord_id = @id", conn);
            deleteDates.Parameters.AddWithValue("@id", id);
            deleteDates.ExecuteNonQuery();

            var deleteMain = new MySqlCommand(
                "DELETE FROM servicerecords WHERE id = @id", conn);
            deleteMain.Parameters.AddWithValue("@id", id);
            deleteMain.ExecuteNonQuery();

            // 3️⃣  Send deletion notification
            string body = $@"
A time-off request has been *deleted*.

Employee   : {rec.LastName}, {rec.FirstName}
Department : {rec.Department}
Shift      : {rec.Shift}
Type       : {rec.TimeOffType}
Dates      : {dateList}

Explanation:
{rec.Explanation}

Deleted on {DateTime.Now:MM/dd/yyyy h:mm tt}
Link to Calendar:
http://192.168.1.6:5000/Calendar
-------------------------------------------------------------------";

            _sharedService.SendEmailWithAttachment(
                "calendar@sintergy.net",    // to calendar
                null, null,
                $"Deleted Time-Off Request: {rec.LastName}, {rec.FirstName}",
                body);

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
                "calendar@sintergy.net",  // to calendar
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

        // CalendarController.cs
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditCalendarEvent(CalendarEventModel m)
        {
            try
            {
                /* 1️⃣  Basic validation */
                if (!ModelState.IsValid)
                    throw new InvalidOperationException("ModelState is invalid");

                /* 2️⃣  Load the existing event (throws if not found) */
                var before = _calendarService.GetCalendarEventById(m.Id)
                             ?? throw new Exception($"No calendar event found with Id {m.Id}");

                /* 3️⃣  Persist the changes */
                _calendarService.UpdateCalendarEvent(m);

                /* 4️⃣  Build the notification e-mail */
                string body = $@"
A calendar event has been *edited*.

--- BEFORE ---
Title      : {before.Title}
Location   : {before.Location}
Date       : {before.Date:MM/dd/yyyy}
Start Time : {before.StartTime:hh\:mm}
End Time   : {before.EndTime:hh\:mm}
Description: {before.Description ?? "(none)"}

--- AFTER ---
Title      : {m.Title}
Location   : {m.Location}
Date       : {m.Date:MM/dd/yyyy}
Start Time : {m.StartTime:hh\:mm}
End Time   : {m.EndTime:hh\:mm}
Description: {m.Description ?? "(none)"}

Edited on {DateTime.Now:MM/dd/yyyy h:mm tt}
Link to Calendar:
http://192.168.1.6:5000/Calendar
-------------------------------------------------------------------";


                /* 5️⃣  Try to send e-mail (swallow any SMTP failure) */
                TrySendMail("calendar@sintergy.net", $"Edited Event: {before.Title}", body);

                /* 6️⃣  All good → HTTP 200 */
                return Ok();
            }
            catch (Exception ex)
            {
                /* Log and surface the full stack trace so we can see it in DevTools */
                _logger.LogError(ex, "EditCalendarEvent failed");
                return StatusCode(500, ex.ToString());
            }
        }



        // helper (add once, anywhere in the class)
        private void TrySendMail(string to, string subject, string body)
        {
            try
            {
                _sharedService.SendEmailWithAttachment(to, null, null, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "E-mail send failed (subject: {Subject})", subject);
                // swallow – we do NOT want the error to bubble up to the HTTP response
            }
        }

        // ────────────────────  DELETE CALENDAR EVENT  ────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteCalendarEvent(int id)
        {
            var ev = _calendarService.GetCalendarEventById(id)
                     ?? throw new Exception($"Event {id} not found");

            _calendarService.DeleteCalendarEvent(id);

            string body = $@"
A calendar event has been *deleted*.

Title      : {ev.Title}
Location   : {ev.Location}
Date       : {ev.Date:MM/dd/yyyy}
Start Time : {ev.StartTime:hh\:mm}
End Time   : {ev.EndTime:hh\:mm}

Deleted on {DateTime.Now:MM/dd/yyyy h:mm tt}
Link to Calendar:
http://192.168.1.6:5000/Calendar
-------------------------------------------------------------------";

            TrySendMail("calendar@sintergy.net", $"Deleted Event: {ev.Title}", body);

            return Ok();
        }

        [HttpGet]
        public IActionResult VerifyAdminPin(string pin)
        {
            const string adminPin = "9412";
            // 200 OK if pin matches, 401 otherwise
            if (pin == adminPin)
                return Ok();
            return Unauthorized();
        }



    }

}
