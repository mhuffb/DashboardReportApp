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
        /* ───────────────────────── INDEX VIEW ───────────────────────── */


        public IActionResult Index()
        {
            var vm = new CalendarIndexViewModel
            {
                Employees = _calendarService.GetEmployees(),
                ServiceRecords = _calendarService.GetServiceRecords()
            };
            return View(vm);
        }



        /* ───────────────────────── SUBMIT (Time‑Off) ───────────────────────── */
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Submit(CalendarModel model, string DatesRequested)
        {
            model.FirstName = model.FirstName?.Trim();
            model.LastName = model.LastName?.Trim();

            if (string.IsNullOrWhiteSpace(model.Email))
            {
                // look up the employee in memory — you already queried the list for the view
                var emp = _calendarService.GetEmployees()
                                          .FirstOrDefault(e => e.FirstName == model.FirstName
                                                            && e.LastName == model.LastName);
                if (emp != null)
                {
                    model.Email = emp.Email;
                    model.ActiveStatus = emp.ActiveStatus;
                    model.DateEmployed = emp.DateEmployed;
                }
            }


            /* parse multiple CSV date list */
            var parsed = new List<DateTime>();
            if (!string.IsNullOrWhiteSpace(DatesRequested))
            {
                foreach (var tok in DatesRequested.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (DateTime.TryParse(tok.Trim(), out var d))
                        parsed.Add(d.Date);
            }
            model.DatesRequested = parsed;
            model.SubmittedOn = DateTime.Now;

            /* auto‑approve when Attribute present */
            bool needsApproval = string.IsNullOrWhiteSpace(model.Attribute) || model.TimeOffType == "Other";
            model.Status = needsApproval ? "Waiting" : "Approved";


            _calendarService.SaveServiceRecord(model);

            /* build e‑mail (only if waiting for approval) */
            if (needsApproval)
            {
                string list = parsed.Any() ? string.Join(", ", parsed.Select(d => d.ToString("MM/dd/yyyy"))) : "(none)";

                string body = $@"A new time‑off request is waiting for approval.

Status     : Waiting
Employee   : {model.LastName}, {model.FirstName}
Department : {model.Department}
Shift      : {model.Shift}
Schedule   : {model.Schedule}
Type       : {model.TimeOffType}
Attribute  : {model.Attribute}
Dates      : {list}

Explanation:
{model.Explanation}

Submitted on {model.SubmittedOn:g}
Link to Calendar:
http://192.168.1.6:5000/Calendar";

                _sharedService.SendEmailWithAttachment("hr@sintergy.net", null, null,
                    $"Time‑Off Request: {model.LastName}, {model.FirstName}", body);

                _sharedService.SendEmailWithAttachment("mhuff@sintergy.net", null, null,
                    $"Time‑Off Request: {model.LastName}, {model.FirstName}", body);
            }

            TempData["Success"] = "Record submitted";
            return RedirectToAction("Index");
        }

        /* ───────────────────────── APPROVE ───────────────────────── */
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Approve(int id, string pin, string occ)
        {
            var pinMap = new Dictionary<string, string>
    {
        { "9412", "Roger Jones" },
        { "8888", "Tom Grieneisen" },
        { "3005", "Andrea Kline" }
    };

            if (!pinMap.TryGetValue(pin, out string approver))
                return BadRequest("Invalid PIN");

            bool emailFailed = false;

            using (var conn = new MySqlConnection(_mysql))
            {
                conn.Open();
                var cmd = new MySqlCommand(
                    "UPDATE servicerecords SET status='Approved', occurrence=@occ, approved_by=@by WHERE id=@id",
                    conn);
                cmd.Parameters.AddWithValue("@occ", occ);
                cmd.Parameters.AddWithValue("@by", approver);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }

            // Build email body
            CalendarModel rec = _calendarService.GetServiceRecordById(id);
            List<DateTime> dates = _calendarService.GetRequestedDates(id);
            string datesText = dates.Any() ? string.Join(", ", dates.Select(d => d.ToString("MM/dd/yyyy"))) : "(none)";

            string voucherLine = occ == "SEPP Voucher required" ? "\n\n⚠ SEPP Voucher required" : "";

            string body = $@"Time‑off request has been *approved*.

Employee   : {rec.LastName}, {rec.FirstName}
Department : {rec.Department}
Shift      : {rec.Shift}
Schedule   : {rec.Schedule}
Type       : {rec.TimeOffType}
Attribute  : {rec.Attribute}
Occurrence : {occ}
Dates      : {datesText}

Approved on {DateTime.Now:MM/dd/yyyy h:mm tt}{voucherLine}";

            // Send to user
            if (!string.IsNullOrWhiteSpace(rec.Email) && rec.Email.Contains("@"))
            {
                try
                {
                    _sharedService.SendEmailWithAttachment(rec.Email, null, null,
                        $"Time‑Off Request: {rec.LastName}, {rec.FirstName}", body);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to send approval email to {rec.Email}");
                    emailFailed = true;
                }
            }
            else
            {
                emailFailed = true;
                _sharedService.PrintPlainText("HPFront", body, 1);
            }

            // Always send internal copy
            try
            {
                _sharedService.SendEmailWithAttachment("calendar@sintergy.net", null, null,
                    $"Time‑Off Request: {rec.LastName}, {rec.FirstName}", body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send calendar approval email.");
                emailFailed = true;
            }

            return Json(new { success = true, emailFailed });
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

        /* ───────────────────────── SCHEDULE EVENT ───────────────────────── */
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ScheduleEvent(string Title, string Location, string Description,
                                           string Date, string StartTime, string EndTime,
                                           string Scheduler,
                                           string Recur, string RecurUntil)
        {
            var first = new CalendarEventModel
            {
                Title = Title?.Trim(),
                Location = Location,
                Description = Description,
                Date = DateTime.Parse(Date),
                StartTime = TimeSpan.Parse(StartTime),
                EndTime = TimeSpan.Parse(EndTime),
                Scheduler = Scheduler
            };

            _calendarService.SaveCalendarEvent(first);

            /* simple recurrence */
            if (!string.IsNullOrWhiteSpace(Recur))
            {
                DateTime until = DateTime.TryParse(RecurUntil, out var u) ? u.Date : first.Date;
                DateTime next = first.Date;
                while (true)
                {
                    next = Recur switch
                    {
                        "Daily" => next.AddDays(1),
                        "Weekly" => next.AddDays(7),
                        "Monthly" => next.AddMonths(1),
                        _ => until.AddDays(1) /* break */
                    };
                    if (next > until) break;
                    first.Date = next;
                    _calendarService.SaveCalendarEvent(first);
                }
            }

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
            const string adminPinRogerJones = "9412";
            const string adminPinAndreaKline = "3005"; // Replace with actual second PIN

            // 200 OK if pin matches either admin PIN, 401 otherwise
            if (pin == adminPinRogerJones || pin == adminPinAndreaKline)
                return Ok();

            return Unauthorized();
        }



        [HttpGet]
        public IActionResult CountSeppVouchers(string pin, int year)
        {
            pin = pin?.Trim();
            Console.WriteLine($"Pin submitted: '{pin}'");

            var validPins = new[] { "9412", "3005", "7777" };
            if (!validPins.Contains(pin))
                return Unauthorized();

            var start = new DateTime(year - 1, 12, 1);
            var end = new DateTime(year, 11, 30);

            var detailList = new List<object>();
            var countPerPerson = new List<object>();

            using (var conn = new MySqlConnection(_mysql))
            {
                conn.Open();

                // 🟦 Detail List Query
                var cmdDetail = new MySqlCommand(@"
SELECT sr.id, sr.fname, sr.lname, sr.department, sr.shift, sr.schedule, d.requested_date
FROM servicerecords sr
JOIN servicerecord_dates d ON sr.id = d.servicerecord_id
WHERE sr.occurrence = 'SEPP Voucher required'
AND d.requested_date BETWEEN @start AND @end
ORDER BY d.requested_date", conn);

                cmdDetail.Parameters.AddWithValue("@start", start);
                cmdDetail.Parameters.AddWithValue("@end", end);

                using (var rdr = cmdDetail.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        detailList.Add(new
                        {
                            Id = rdr.GetInt32("id"),
                            FirstName = rdr["fname"].ToString(),
                            LastName = rdr["lname"].ToString(),
                            Department = rdr["department"].ToString(),
                            Shift = rdr["shift"].ToString(),
                            Schedule = rdr["schedule"].ToString(),
                            Date = Convert.ToDateTime(rdr["requested_date"]).ToString("MM/dd/yyyy")
                        });
                    }
                }

                // 🟩 Count Per Person Query
                // 🟩 Count Per Person Query
                var cmdCount = new MySqlCommand(@"
SELECT sr.fname, sr.lname, COUNT(*) AS count
FROM servicerecords sr
JOIN servicerecord_dates d ON sr.id = d.servicerecord_id
WHERE sr.occurrence = 'SEPP Voucher required'
AND d.requested_date BETWEEN @start AND @end
GROUP BY sr.fname, sr.lname
ORDER BY count DESC", conn);

                // 🔥 🔥 Add parameters for cmdCount too!
                cmdCount.Parameters.AddWithValue("@start", start);
                cmdCount.Parameters.AddWithValue("@end", end);

                using (var rdr = cmdCount.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        countPerPerson.Add(new
                        {
                            FirstName = rdr["fname"].ToString(),
                            LastName = rdr["lname"].ToString(),
                            Count = Convert.ToInt32(rdr["count"])
                        });
                    }
                }

            }

            return Json(new
            {
                yearStart = start.ToString("MM/dd/yyyy"),
                yearEnd = end.ToString("MM/dd/yyyy"),
                totalOccurrences = detailList.Count,
                countPerPerson = countPerPerson,
                details = detailList
            });
        }


    }

}
