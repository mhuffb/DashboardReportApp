using DashboardReportApp.Models;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;

namespace DashboardReportApp.Services
{
    public class CalendarService
    {
        private readonly string _sqlServerConnection;  
        private readonly string _mysqlConnection;

        public CalendarService(IConfiguration config)
        {
            _sqlServerConnection = config.GetConnectionString("SqlServerConnectionsinTSQL");
            _mysqlConnection = config.GetConnectionString("MySQLConnection");
        }

        /* ─────────────── EMPLOYEES ─────────────── */
        public List<CalendarModel> GetEmployees()
        {
            var list = new List<CalendarModel>();

            const string sql = @"
                SELECT fname, lname, date_employed, active_status, email, vac_balance
                FROM dbo.employee
                WHERE active_status = 'A'";

            using var conn = new SqlConnection(_sqlServerConnection);
            using var cmd = new SqlCommand(sql, conn);
            conn.Open();

            using var rdr = cmd.ExecuteReader();
            int ordF = rdr.GetOrdinal("fname");
            int ordL = rdr.GetOrdinal("lname");
            int ordD = rdr.GetOrdinal("date_employed");
            int ordA = rdr.GetOrdinal("active_status");
            int ordE = rdr.GetOrdinal("email");
            int ordV = rdr.GetOrdinal("vac_balance");

            while (rdr.Read())
            {
                var m = new CalendarModel
                {
                    FirstName = rdr.IsDBNull(ordF) ? "" : rdr.GetString(ordF).Trim(),
                    LastName = rdr.IsDBNull(ordL) ? "" : rdr.GetString(ordL).Trim(),
                    DateEmployed = rdr.IsDBNull(ordD) ? DateTime.MinValue : rdr.GetDateTime(ordD),
                    ActiveStatus = rdr.IsDBNull(ordA) ? "" : rdr.GetString(ordA).Trim(),
                    Email = rdr.IsDBNull(ordE) ? "" : rdr.GetString(ordE).Trim(),
                    VacationBalance = rdr.IsDBNull(ordV) ? 0m : Convert.ToDecimal(rdr.GetValue(ordV))
                };
                list.Add(m);
            }

            // ✅ Keep your manual test employee
            list.Add(new CalendarModel
            {
                FirstName = "User",
                LastName = "Test",
                DateEmployed = DateTime.Today,
                ActiveStatus = "A",
                Email = "asdfff@sintergy.net",
                VacationBalance = 999m
            });

            return list;
        }
        /* ─────────────── SAVE SERVICE RECORD ─────────────── */
        public void SaveServiceRecord(CalendarModel m)
        {
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();
            using var tran = conn.BeginTransaction();
            var cmd = new MySqlCommand(@"INSERT INTO servicerecords
(fname,lname,date_employed,active_status,email,
 vac_balance,department,shift,schedule,attribute,explanation,
 time_off_type,status,occurrence,submitted_on)
VALUES
(@fn,@ln,@de,@as,@em,@vb,@dept,@shift,@sched,@attr,@expl,
 @type,@status,@occ,@sub); SELECT LAST_INSERT_ID();", conn, tran);

            cmd.Parameters.AddWithValue("@fn", m.FirstName);
            cmd.Parameters.AddWithValue("@ln", m.LastName);
            cmd.Parameters.AddWithValue("@de", m.DateEmployed);
            cmd.Parameters.AddWithValue("@as", m.ActiveStatus);
            cmd.Parameters.AddWithValue("@em", m.Email);
            cmd.Parameters.AddWithValue("@vb", m.VacationBalance);
            cmd.Parameters.AddWithValue("@dept", m.Department);
            cmd.Parameters.AddWithValue("@shift", m.Shift);
            cmd.Parameters.AddWithValue("@sched", m.Schedule);
            cmd.Parameters.AddWithValue("@attr", m.Attribute);
            cmd.Parameters.AddWithValue("@expl", m.Explanation);
            cmd.Parameters.AddWithValue("@type", m.TimeOffType);
            cmd.Parameters.AddWithValue("@status", m.Status);
            cmd.Parameters.AddWithValue("@occ", DBNull.Value);
            cmd.Parameters.AddWithValue("@sub", m.SubmittedOn);

            int newId = Convert.ToInt32(cmd.ExecuteScalar());

            foreach (var d in m.DatesRequested)
            {
                var dc = new MySqlCommand("INSERT INTO servicerecord_dates(servicerecord_id,requested_date) VALUES(@id,@dt)", conn, tran);
                dc.Parameters.AddWithValue("@id", newId);
                dc.Parameters.AddWithValue("@dt", d);
                dc.ExecuteNonQuery();
            }
            tran.Commit();
        }

        public List<CalendarModel> GetServiceRecords()
        {
            var results = new List<CalendarModel>();

            const string sql = @"
SELECT sr.id,
       sr.status,
       sr.fname,
       sr.lname,
       sr.vac_balance,
       sr.department,
       sr.shift,            -- NEW
       sr.schedule,         -- NEW
       sr.submitted_on,     -- NEW
       sr.attribute,
       sr.time_off_type,
       sr.explanation,
       d.requested_date,
       sr.approved_by
FROM   servicerecords       sr
JOIN   servicerecord_dates  d  ON sr.id = d.servicerecord_id
ORDER  BY sr.id, d.requested_date";

            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            using var rdr = cmd.ExecuteReader();

            CalendarModel cur = null;
            int lastId = -1;

            while (rdr.Read())
            {
                int id = rdr.GetInt32("id");
                if (id != lastId)
                {
                    /* ── create a new CalendarModel and copy EVERY column ── */
                    cur = new CalendarModel
                    {
                        Id = id,
                        Status = rdr["status"].ToString(),
                        FirstName = rdr["fname"].ToString(),
                        LastName = rdr["lname"].ToString(),
                        VacationBalance = rdr.GetDecimal("vac_balance"),
                        Department = rdr["department"].ToString(),
                        Shift = rdr["shift"].ToString(),       // NEW
                        Schedule = rdr["schedule"].ToString(),    // NEW
                        SubmittedOn = rdr.GetDateTime("submitted_on"), // NEW
                        Attribute = rdr["attribute"].ToString(),
                        TimeOffType = rdr["time_off_type"].ToString(),
                        Explanation = rdr["explanation"].ToString(),
                        DatesRequested = new List<DateTime>(),
                        ApprovedBy = rdr["approved_by"]?.ToString(),

                    };
                    results.Add(cur);
                    lastId = id;
                }

                /* add one requested date to the list */
                cur.DatesRequested.Add(rdr.GetDateTime("requested_date"));
            }
            return results;
        }


        public CalendarModel GetServiceRecordById(int id)
        {
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();
            var cmd = new MySqlCommand(@"
SELECT id, fname, lname, email, vac_balance,
          department, shift, schedule, time_off_type,
          explanation ,
       attribute   
FROM servicerecords WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return null;

            return new CalendarModel
            {
                Id = rdr.GetInt32("id"),
                FirstName = rdr["fname"].ToString(),
                LastName = rdr["lname"].ToString(),
                Email = rdr["email"].ToString(),
                VacationBalance = Convert.ToDecimal(rdr["vac_balance"]),
                Department = rdr["department"].ToString(),
                Shift = rdr["shift"].ToString(),
                Schedule = rdr["schedule"].ToString(),
                TimeOffType = rdr["time_off_type"].ToString(),
                Explanation = rdr["explanation"].ToString(),
                Attribute = rdr["attribute"]?.ToString() ?? ""
            };
        }

        /* ─────────────────────────── BLUE CALENDAR EVENTS ─────────────────────────── */

        // Services/CalendarService.cs

        public void SaveCalendarEvent(CalendarEventModel m)
        {
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();
            var cmd = new MySqlCommand(@"
INSERT INTO cal_events
(title,location,description,event_date,start_time,end_time,scheduler,submitted_on,
 series_id,is_seed,recur_rule,recur_until)
VALUES(@t,@l,@d,@dt,@st,@et,@sch,@sub,
       @sid,@seed,@rule,@until)", conn);

            cmd.Parameters.AddWithValue("@t", m.Title);
            cmd.Parameters.AddWithValue("@l", m.Location);
            cmd.Parameters.AddWithValue("@d", m.Description);
            cmd.Parameters.AddWithValue("@dt", m.Date.Date);
            cmd.Parameters.AddWithValue("@st", m.StartTime);
            cmd.Parameters.AddWithValue("@et", m.EndTime);
            cmd.Parameters.AddWithValue("@sch", m.Scheduler);
            cmd.Parameters.AddWithValue("@sub", DateTime.Now);

            cmd.Parameters.AddWithValue("@sid", (object?)m.SeriesId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@seed", m.IsSeed ? 1 : 0);
            cmd.Parameters.AddWithValue("@rule", m.RecurRule ?? "None");
            cmd.Parameters.AddWithValue("@until", (object?)m.RecurUntil?.Date ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public IEnumerable<CalendarEventModel> GetCalendarEvents()
        {
            var list = new List<CalendarEventModel>();
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();

            var cmd = new MySqlCommand(@"
SELECT id,title,location,description,event_date,start_time,end_time,
       series_id,is_seed,recur_rule,recur_until
FROM cal_events", conn);

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                list.Add(new CalendarEventModel
                {
                    Id = rdr.GetInt32("id"),
                    Title = rdr["title"].ToString(),
                    Location = rdr["location"].ToString(),
                    Description = rdr["description"].ToString(),
                    Date = Convert.ToDateTime(rdr["event_date"]),
                    StartTime = rdr.GetTimeSpan("start_time"),
                    EndTime = rdr.GetTimeSpan("end_time"),
                    SeriesId = rdr["series_id"] as string,
                    IsSeed = Convert.ToInt32(rdr["is_seed"]) == 1,
                    RecurRule = rdr["recur_rule"]?.ToString() ?? "None",
                    RecurUntil = rdr["recur_until"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["recur_until"])
                });
            }
            return list;
        }

        public CalendarEventModel GetCalendarEventById(int id)
        {
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();

            var cmd = new MySqlCommand(@"
SELECT id,title,location,description,event_date,start_time,end_time,
       series_id,is_seed,recur_rule,recur_until
FROM cal_events WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return null;

            return new CalendarEventModel
            {
                Id = rdr.GetInt32("id"),
                Title = rdr["title"].ToString(),
                Location = rdr["location"].ToString(),
                Description = rdr["description"].ToString(),
                Date = Convert.ToDateTime(rdr["event_date"]),
                StartTime = rdr.GetTimeSpan("start_time"),
                EndTime = rdr.GetTimeSpan("end_time"),
                SeriesId = rdr["series_id"] as string,
                IsSeed = Convert.ToInt32(rdr["is_seed"]) == 1,
                RecurRule = rdr["recur_rule"]?.ToString() ?? "None",
                RecurUntil = rdr["recur_until"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["recur_until"])
            };
        }


        // Update ALL in series (title/location/desc/date delta/time ranges)
        public void UpdateCalendarSeries_All(string seriesId, string title, string location, string description,
                                             TimeSpan start, TimeSpan end)
        {
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();
            var cmd = new MySqlCommand(@"
UPDATE cal_events
   SET title=@t, location=@l, description=@d,
       start_time=@st, end_time=@et
 WHERE series_id=@sid", conn);

            cmd.Parameters.AddWithValue("@t", title);
            cmd.Parameters.AddWithValue("@l", location);
            cmd.Parameters.AddWithValue("@d", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@st", start);
            cmd.Parameters.AddWithValue("@et", end);
            cmd.Parameters.AddWithValue("@sid", seriesId);
            cmd.ExecuteNonQuery();
        }

        // Delete ENTIRE series
        public void DeleteCalendarSeries_All(string seriesId)
        {
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();
            var cmd = new MySqlCommand("DELETE FROM cal_events WHERE series_id=@sid", conn);
            cmd.Parameters.AddWithValue("@sid", seriesId);
            cmd.ExecuteNonQuery();
        }

        // Delete FUTURE in series (from a pivot date inclusive)
        public void DeleteCalendarSeries_Future(string seriesId, DateTime pivotInclusive)
        {
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();
            var cmd = new MySqlCommand(@"
DELETE FROM cal_events
 WHERE series_id=@sid AND event_date >= @pivot", conn);
            cmd.Parameters.AddWithValue("@sid", seriesId);
            cmd.Parameters.AddWithValue("@pivot", pivotInclusive.Date);
            cmd.ExecuteNonQuery();
        }


        /* UPDATE event */
        public void UpdateCalendarEvent(CalendarEventModel m)
        {
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();

            var cmd = new MySqlCommand(@"
UPDATE cal_events
SET title=@t,location=@l,description=@d,
    event_date=@dt,start_time=@st,end_time=@et
WHERE id=@id", conn);

            cmd.Parameters.AddWithValue("@t", m.Title);
            cmd.Parameters.AddWithValue("@l", m.Location);
            cmd.Parameters.AddWithValue("@d", m.Description);
            cmd.Parameters.AddWithValue("@dt", m.Date.Date);
            cmd.Parameters.AddWithValue("@st", m.StartTime);
            cmd.Parameters.AddWithValue("@et", m.EndTime);
            cmd.Parameters.AddWithValue("@id", m.Id);

            cmd.ExecuteNonQuery();
        }

        /* DELETE event */
        public void DeleteCalendarEvent(int id)
        {
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();
            var cmd = new MySqlCommand("DELETE FROM cal_events WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public List<DateTime> GetRequestedDates(int serviceRecordId)
        {
            var dates = new List<DateTime>();

            using (var conn = new MySqlConnection(_mysqlConnection))
            {
                conn.Open();
                using var cmd = new MySqlCommand(
                    "SELECT requested_date FROM servicerecord_dates WHERE servicerecord_id = @id",
                    conn);
                cmd.Parameters.AddWithValue("@id", serviceRecordId);

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    // assuming the column is DATE or DATETIME
                    dates.Add(rdr.GetDateTime("requested_date").Date);
                }
            }

            return dates;
        }
    }
}

