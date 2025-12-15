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


        private readonly Dictionary<string, string> _approverByPin;
        private readonly HashSet<string> _adminPins;
        private readonly HashSet<string> _voucherReportPins;


        private readonly string _emailOverrideAllTo;
        private readonly string _emailCalendarTo;
        private readonly string _emailHrTo;

        private readonly string _emailDevTo;
        public CalendarController(CalendarService calendarService,
                              SharedService sharedService,
                              IConfiguration cfg,
                              ILogger<CalendarController> logger)
        {
            _calendarService = calendarService;
            _sharedService = sharedService;

            _logger = logger;      // ← store
            _mysql = cfg.GetConnectionString("MySQLConnection");

            var emailSec = cfg.GetSection("Email");
            _emailOverrideAllTo = emailSec["OverrideAllTo"] ?? "";
            _emailCalendarTo = emailSec["CalendarTo"] ?? "";
            _emailHrTo = emailSec["HrTo"] ?? "";
            _emailDevTo = emailSec["DevTo"] ?? "";

            // ─── Approvals config  ───
            var approvals = cfg.GetSection("Approvals");
            _approverByPin = approvals.GetSection("Pins")
                .GetChildren()
                .Select(s => new { Pin = (s["Pin"] ?? "").Trim(), Name = (s["Name"] ?? "").Trim() })
                .Where(x => !string.IsNullOrWhiteSpace(x.Pin) && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Pin)
                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);

            _adminPins = approvals.GetSection("AdminPins").Get<string[]>() is { Length: > 0 } a
                ? new HashSet<string>(a.Select(p => p.Trim()), StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            _voucherReportPins = approvals.GetSection("VoucherReportPins").Get<string[]>() is { Length: > 0 } v
                ? new HashSet<string>(v.Select(p => p.Trim()), StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
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

            /* Approval rules:
  * - Paid Vacation: ALWAYS needs approval
  * - Other: needs approval
  * - Otherwise: needs approval if Attribute is empty
  */
            bool isPaidVacation = string.Equals(model.TimeOffType, "Paid Vacation", StringComparison.OrdinalIgnoreCase);
            bool isOther = string.Equals(model.TimeOffType, "Other", StringComparison.OrdinalIgnoreCase);

            bool needsApproval = isPaidVacation || isOther || string.IsNullOrWhiteSpace(model.Attribute);
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
http://192.168.1.9:5000/Calendar";

                var tos = new[] { _emailHrTo, _emailDevTo }.Where(x => !string.IsNullOrWhiteSpace(x));
                SendConfiguredBatch(new[] { _emailHrTo, _emailDevTo },
        $"Time-Off Request: {model.LastName}, {model.FirstName}", body);
            }

            TempData["Success"] = "Record submitted";
            return RedirectToAction("Index");
        }

        /* ───────────────────────── APPROVE ───────────────────────── */
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Approve(int id, string pin, string occ)
        {
            if (!TryResolveApprover(pin, out var approver))
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

            var rec = _calendarService.GetServiceRecordById(id);
            var dates = _calendarService.GetRequestedDates(id);
            var datesText = dates.Any() ? string.Join(", ", dates.Select(d => d.ToString("MM/dd/yyyy"))) : "(none)";
            var voucherLine = occ == "SEPP Voucher required" ? "\n\n⚠ SEPP Voucher required" : "";
            var attrText = string.IsNullOrWhiteSpace(rec.Attribute) ? "(none)" : rec.Attribute;
            var explText = string.IsNullOrWhiteSpace(rec.Explanation) ? "(none)" : rec.Explanation;  // NEW
            var subject = $"Time-Off Request: {rec.LastName}, {rec.FirstName}";
            var body = $@"Time-off request has been *approved*.

Employee   : {rec.LastName}, {rec.FirstName}
Department : {rec.Department}
Shift      : {rec.Shift}
Schedule   : {rec.Schedule}
Type       : {rec.TimeOffType}
Attribute  : {attrText}
Explanation: {explText}
Occurrence : {occ}
Dates      : {datesText}

Approved by {approver} on {DateTime.Now:MM/dd/yyyy h:mm tt}{voucherLine}";


            // Recipients: employee + calendar (deduped; also dedupes when OverrideAllTo collapses)
            var toList = new List<string?>();
            if (!string.IsNullOrWhiteSpace(rec.Email) && rec.Email.Contains("@"))
                toList.Add(rec.Email);
            else
                emailFailed = true; // no user email on file

            toList.Add(_emailCalendarTo);

            if (!toList.Any(t => !string.IsNullOrWhiteSpace(t)))
                _sharedService.PrintPlainText("HPFront", body, 1); // last resort

            SendConfiguredBatch(toList, subject, body);

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
                dates = dates,
                occurrence = rec.Occurrence   
            });

        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditRecord(int id, string type, string shift, string explanation, string dates,
    string occurrence)
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
           shift         = @shift,
           explanation   = @expl,
           occurrence    = @occ
     WHERE id = @id", conn, tran);

            update.Parameters.AddWithValue("@type", type);
            update.Parameters.AddWithValue("@shift", shift);
            update.Parameters.AddWithValue("@expl", explanation);
            update.Parameters.AddWithValue("@occ", string.IsNullOrWhiteSpace(occurrence) || occurrence == "No occurrence"
                                                    ? (object)DBNull.Value
                                                    : occurrence);
            update.Parameters.AddWithValue("@id", id);
            update.ExecuteNonQuery();


            var del = new MySqlCommand("DELETE FROM servicerecord_dates WHERE servicerecord_id = @id", conn, tran);
            del.Parameters.AddWithValue("@id", id);
            del.ExecuteNonQuery();

            // If edited to Paid Vacation, ensure it requires approval again
            if (string.Equals(type, "Paid Vacation", StringComparison.OrdinalIgnoreCase))
            {
                var forceWaiting = new MySqlCommand(@"
        UPDATE servicerecords
           SET status = 'Waiting', approved_by = NULL
         WHERE id = @id;", conn, tran);
                forceWaiting.Parameters.AddWithValue("@id", id);
                forceWaiting.ExecuteNonQuery();
            }

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
Occurrence   : {original.Occurrence ?? "No occurrence"}

--- AFTER ---
Type         : {type}
Shift        : {shift}
Dates        : {newDatesText}
Explanation  : {explanation}
Occurrence   : {occurrence}

Edited on    : {DateTime.Now:MM/dd/yyyy h:mm tt}
Link to Calendar:
http://Sintergydc2024.local:5000/Calendar
-------------------------------------------------------------------";


            SendConfigured(_emailCalendarTo,
    $"Edited Time-Off Request: {original.LastName}, {original.FirstName}", body);


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
http://192.168.1.9:5000/Calendar
-------------------------------------------------------------------";

            SendConfigured(_emailCalendarTo,
    $"Deleted Time-Off Request: {rec.LastName}, {rec.FirstName}", body);


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
            var seed = new CalendarEventModel
            {
                Title = Title?.Trim(),
                Location = Location,
                Description = Description,
                Date = DateTime.Parse(Date),
                StartTime = TimeSpan.Parse(StartTime),
                EndTime = TimeSpan.Parse(EndTime),
                Scheduler = Scheduler,
                RecurRule = string.IsNullOrWhiteSpace(Recur) || Recur == "\\" ? "None" : Recur,
                RecurUntil = DateTime.TryParse(RecurUntil, out var u) ? u.Date : (DateTime?)null
            };

            // NEW: SeriesId for any recurrence (or null for None)
            var seriesId = (seed.RecurRule == "None") ? null : Guid.NewGuid().ToString();

            // Save first
            _calendarService.SaveCalendarEvent(new CalendarEventModel
            {
                Title = seed.Title,
                Location = seed.Location,
                Description = seed.Description,
                Date = seed.Date,
                StartTime = seed.StartTime,
                EndTime = seed.EndTime,
                Scheduler = seed.Scheduler,
                SeriesId = seriesId,
                IsSeed = true,
                RecurRule = seed.RecurRule,
                RecurUntil = seed.RecurUntil
            });

            var occurrences = new List<DateTime> { seed.Date };

            if (seed.RecurRule != "None" && seed.RecurUntil.HasValue)
            {
                var until = seed.RecurUntil.Value.Date;
                var next = seed.Date.Date;

                while (true)
                {
                    next = seed.RecurRule switch
                    {
                        "Daily" => next.AddDays(1),
                        "Weekly" => next.AddDays(7),
                        "Monthly" => next.AddMonths(1),
                        _ => until.AddDays(1)
                    };
                    if (next > until) break;

                    occurrences.Add(next);
                    _calendarService.SaveCalendarEvent(new CalendarEventModel
                    {
                        Title = seed.Title,
                        Location = seed.Location,
                        Description = seed.Description,
                        Date = next,
                        StartTime = seed.StartTime,
                        EndTime = seed.EndTime,
                        Scheduler = seed.Scheduler,
                        SeriesId = seriesId,
                        IsSeed = false,
                        RecurRule = seed.RecurRule,
                        RecurUntil = seed.RecurUntil
                    });
                }
            }

            // ───── Email notification ─────
            try
            {
                string times = $"{seed.StartTime:hh\\:mm}-{seed.EndTime:hh\\:mm}";
                string list = string.Join("\n", occurrences.Select(d => $"• {d:MM/dd/yyyy} {times}"));

                string body = $@"
A calendar event has been *scheduled*.

Title      : {seed.Title}
Location   : {seed.Location}
Scheduler  : {seed.Scheduler}
Description: {(string.IsNullOrWhiteSpace(seed.Description) ? "(none)" : seed.Description)}

Occurrences:
{list}

Created on : {DateTime.Now:MM/dd/yyyy h:mm tt}
Link to Calendar:
http://192.168.1.9:5000/Calendar
-------------------------------------------------------------------";

                // main notification
                SendConfigured(_emailCalendarTo, $"New Event: {seed.Title}", body);

                // try to notify the scheduler directly (look up email from Employees)
                // scheduler direct (if found)
                var parts = (seed.Scheduler ?? "").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var emp = _calendarService.GetEmployees()
                                .FirstOrDefault(e => e.FirstName == parts[0] && e.LastName == parts[1]);
                    if (!string.IsNullOrWhiteSpace(emp?.Email))
                        SendConfigured(emp.Email, $"Your event has been scheduled: {seed.Title}", body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send new-event email");
                // swallow (same approach as your TrySendMail helper): don’t fail the HTTP response
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
            // CalendarController.GetEvents()
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
                    reqDays = 0,

                    // NEW: carry series metadata to the client
                    seriesId = e.SeriesId,
                    occurDate = e.Date.ToString("yyyy-MM-dd")
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
            try
            {
                // We are not using ModelState constraints here because we only post a subset
                // of the full CalendarEventModel from the SweetAlert form.

                // 1️⃣ Grab raw strings from the form
                var dateStr = Request.Form["Date"];
                var startStr = Request.Form["StartTime"];
                var endStr = Request.Form["EndTime"];

                if (!DateTime.TryParse(dateStr, out var newDate))
                    throw new Exception($"Bad Date value: '{dateStr}'");

                if (!TimeSpan.TryParse(startStr, out var newStart))
                    throw new Exception($"Bad StartTime value: '{startStr}'");

                if (!TimeSpan.TryParse(endStr, out var newEnd))
                    throw new Exception($"Bad EndTime value: '{endStr}'");

                // 2️⃣ Load the existing event (to keep Scheduler, Series, Recur, etc.)
                var before = _calendarService.GetCalendarEventById(m.Id)
                             ?? throw new Exception($"No calendar event found with Id {m.Id}");

                // 3️⃣ Build an updated copy with all important fields populated
                var updated = new CalendarEventModel
                {
                    Id = before.Id,
                    Title = m.Title?.Trim(),
                    Location = m.Location,
                    Description = m.Description,
                    Date = newDate.Date,
                    StartTime = newStart,
                    EndTime = newEnd,

                    // keep existing metadata
                    Scheduler = before.Scheduler,
                    SeriesId = before.SeriesId,
                    IsSeed = before.IsSeed,
                    RecurRule = before.RecurRule,
                    RecurUntil = before.RecurUntil,
                    SubmittedOn = before.SubmittedOn
                };

                // 4️⃣ Persist the changes
                _calendarService.UpdateCalendarEvent(updated);

                // 5️⃣ Pre-format times as strings (avoid inline format specifiers that blew up)
                string beforeStart = before.StartTime.ToString(@"hh\:mm");
                string beforeEnd = before.EndTime.ToString(@"hh\:mm");
                string afterStart = updated.StartTime.ToString(@"hh\:mm");
                string afterEnd = updated.EndTime.ToString(@"hh\:mm");

                // 6️⃣ Build the notification e-mail
                string body = $@"
A calendar event has been *edited*.

--- BEFORE ---
Title      : {before.Title}
Location   : {before.Location}
Date       : {before.Date:MM/dd/yyyy}
Start Time : {beforeStart}
End Time   : {beforeEnd}
Description: {(before.Description ?? "(none)")}

--- AFTER ---
Title      : {updated.Title}
Location   : {updated.Location}
Date       : {updated.Date:MM/dd/yyyy}
Start Time : {afterStart}
End Time   : {afterEnd}
Description: {(updated.Description ?? "(none)")}

Edited on {DateTime.Now:MM/dd/yyyy h:mm tt}
Link to Calendar:
http://192.168.1.9:5000/Calendar
-------------------------------------------------------------------";

                SendConfigured(_emailCalendarTo, $"Edited Event: {before.Title}", body);

                // 7️⃣ All good
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EditCalendarEvent failed");
                // Return the exception text so your JS sees 500 and you can view details in network tab
                return StatusCode(500, ex.ToString());
            }
        }
        // ────────────────────  EDIT CALENDAR SERIES (title/location/desc/times)  ────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditCalendarSeries(
            string seriesId,
            string Title,
            string Location,
            string Description,
            string StartTime,
            string EndTime)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(seriesId))
                    return BadRequest("Missing seriesId");

                if (!TimeSpan.TryParse(StartTime, out var st))
                    return BadRequest($"Bad StartTime '{StartTime}'");

                if (!TimeSpan.TryParse(EndTime, out var et))
                    return BadRequest($"Bad EndTime '{EndTime}'");

                // Update all events in this series
                _calendarService.UpdateCalendarSeries_All(
                    seriesId,
                    Title?.Trim(),
                    Location,
                    Description,
                    st,
                    et
                );

                // Optional: e-mail summary
                string stStr = st.ToString(@"hh\:mm");
                string etStr = et.ToString(@"hh\:mm");

                string body = $@"
A calendar event series has been *edited*.

Series Id  : {seriesId}
Title      : {Title}
Location   : {Location}
Time Range : {stStr} - {etStr}
Description: {(string.IsNullOrWhiteSpace(Description) ? "(none)" : Description)}

Edited on {DateTime.Now:MM/dd/yyyy h:mm tt}
Link to Calendar:
http://192.168.1.9:5000/Calendar
-------------------------------------------------------------------";

                SendConfigured(_emailCalendarTo, $"Edited Event Series: {Title}", body);

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EditCalendarSeries failed (seriesId={SeriesId})", seriesId);
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
http://192.168.1.9:5000/Calendar
-------------------------------------------------------------------";

            SendConfigured(_emailCalendarTo, $"Deleted Event: {ev.Title}", body);


            return Ok();
        }

        [HttpGet]
public IActionResult VerifyAdminPin(string pin)
{
    return _adminPins.Contains((pin ?? "").Trim()) ? Ok() : Unauthorized();
}




        [HttpGet]
        public IActionResult CountSeppVouchers(string pin, int year)
        {
            pin = pin?.Trim();
            Console.WriteLine($"Pin submitted: '{pin}'");

            var validPins = new[] { "9412", "3005", "8888" };
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
       

        // Delete ENTIRE series
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteCalendarSeries_All(string seriesId)
        {
            if (string.IsNullOrWhiteSpace(seriesId)) return BadRequest("Missing seriesId");
            _calendarService.DeleteCalendarSeries_All(seriesId);
            SendConfigured(_emailCalendarTo, "Deleted Event Series",
    $@"Series {seriesId} deleted (all occurrences).");

           

            return Ok();
        }

        // Delete FUTURE (and today) from a pivot occurrence
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteCalendarSeries_Future(string seriesId, string pivotDate)
        {
            if (string.IsNullOrWhiteSpace(seriesId)) return BadRequest("Missing seriesId");
            if (!DateTime.TryParse(pivotDate, out var pivot)) return BadRequest("Bad pivot date");
            _calendarService.DeleteCalendarSeries_Future(seriesId, pivot.Date);
            SendConfigured(_emailCalendarTo, "Deleted Future Series",
               $@"Series {seriesId} deleted from {pivot:MM/dd/yyyy} onward.");
            return Ok();
        }

        private bool TryResolveApprover(string pin, out string approver)
    => _approverByPin.TryGetValue((pin ?? "").Trim(), out approver);

        // Core send (no override logic here)
        private void DoSend(string to, string subject, string body, string? cc = null, string? bcc = null)
        {
            try
            {
                _sharedService.SendEmailWithAttachment(to, cc, bcc, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email send failed: to='{To}', subject='{Subject}'", to, subject);
            }
        }

        // Existing single-recipient helper (kept for convenience)
        private void SendConfigured(string to, string subject, string body, string? cc = null, string? bcc = null)
        {
            var effectiveTo = string.IsNullOrWhiteSpace(_emailOverrideAllTo) ? to : _emailOverrideAllTo;
            var effectiveCc = string.IsNullOrWhiteSpace(_emailOverrideAllTo) ? cc : null;
            var effectiveBcc = string.IsNullOrWhiteSpace(_emailOverrideAllTo) ? bcc : null;
            DoSend(effectiveTo, subject, body, effectiveCc, effectiveBcc);
        }

        // NEW: batch send with de-duplication AFTER override is applied
        private void SendConfiguredBatch(IEnumerable<string?> tos, string subject, string body)
        {
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in tos.Where(x => !string.IsNullOrWhiteSpace(x))!)
            {
                var dest = string.IsNullOrWhiteSpace(_emailOverrideAllTo) ? raw! : _emailOverrideAllTo;
                if (unique.Add(dest))
                    DoSend(dest, subject, body);
            }
        }

    }

}
