using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;

namespace DashboardReportApp.Services
{
    public class CalendarService
    {
        private readonly string _dataflexConnection;
        private readonly string _mysqlConnection;

        public CalendarService(IConfiguration config)
        {
            _dataflexConnection = config.GetConnectionString("DataflexConnection");
            _mysqlConnection = config.GetConnectionString("MySQLConnection");
        }

        /* ───────────────────────── EMPLOYEES ───────────────────────── */
        public List<CalendarModel> GetEmployees()
        {
            var employees = new List<CalendarModel>();
            using var connection = new OdbcConnection(_dataflexConnection);
            connection.Open();

            var cmd = new OdbcCommand(
                "SELECT fname,lname,date_employed,active_status,email,vac_balance " +
                "FROM employee WHERE active_status='A'", connection);

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                employees.Add(new CalendarModel
                {
                    FirstName = rdr["fname"].ToString(),
                    LastName = rdr["lname"].ToString(),
                    DateEmployed = Convert.ToDateTime(rdr["date_employed"]),
                    ActiveStatus = rdr["active_status"].ToString(),
                    Email = rdr["email"].ToString(),
                    VacationBalance = Convert.ToDecimal(rdr["vac_balance"])
                });
            }
            return employees;
        }

        /* ─────────────────────────── TIME-OFF REQUESTS ─────────────────────────── */
        public void SaveServiceRecord(CalendarModel m)
        {
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();
            using var tran = conn.BeginTransaction();

            var cmd = new MySqlCommand(@"
INSERT INTO servicerecords
(fname,lname,date_employed,active_status,email,
 vac_balance,department,shift,schedule,attribute,explanation,
 time_off_type,status,submitted_on)
VALUES
(@fn,@ln,@de,@as,@em,@vb,@dept,@shift,@sched,@attr,@expl,
 @type,'Waiting',@sub);
SELECT LAST_INSERT_ID();", conn, tran);

            cmd.Parameters.AddWithValue("@fn", m.FirstName?.Trim());
            cmd.Parameters.AddWithValue("@ln", m.LastName?.Trim());
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
            cmd.Parameters.AddWithValue("@sub", m.SubmittedOn);

            int newId = Convert.ToInt32(cmd.ExecuteScalar());

            foreach (var d in m.DatesRequested)
            {
                var dc = new MySqlCommand(
                    "INSERT INTO servicerecord_dates(servicerecord_id,requested_date) VALUES(@id,@dt)",
                    conn, tran);
                dc.Parameters.AddWithValue("@id", newId);
                dc.Parameters.AddWithValue("@dt", d);
                dc.ExecuteNonQuery();
            }

            tran.Commit();
        }

        public List<CalendarModel> GetServiceRecords()
        {
            var results = new List<CalendarModel>();

            string sql = @"
SELECT sr.id,sr.status,
       sr.fname,sr.lname,sr.vac_balance,
       sr.department,sr.attribute,sr.time_off_type, sr.explanation, 
       d.requested_date
FROM servicerecords sr
JOIN servicerecord_dates d ON sr.id=d.servicerecord_id
ORDER BY sr.id,d.requested_date";

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
                    cur = new CalendarModel
                    {
                        Id = id,
                        Status = rdr["status"].ToString(),
                        FirstName = rdr["fname"].ToString(),
                        LastName = rdr["lname"].ToString(),
                        VacationBalance = Convert.ToDecimal(rdr["vac_balance"]),
                        Explanation = rdr["explanation"].ToString(),
                        Department = rdr["department"].ToString(),
                        Attribute = rdr["attribute"].ToString(),
                        TimeOffType = rdr["time_off_type"].ToString(),
                        DatesRequested = new List<DateTime>()
                    };
                    results.Add(cur);
                    lastId = id;
                }
                cur.DatesRequested.Add(Convert.ToDateTime(rdr["requested_date"]));
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
          explanation 
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
                Explanation = rdr["explanation"].ToString()
            };
        }

        /* ─────────────────────────── BLUE CALENDAR EVENTS ─────────────────────────── */

        /* SAVE one event row */
        public void SaveCalendarEvent(CalendarEventModel m)
        {
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();

            var cmd = new MySqlCommand(@"
INSERT INTO cal_events
(title,location,description,event_date,start_time,end_time,submitted_on)
VALUES(@t,@l,@d,@dt,@st,@et,@sub)", conn);

            cmd.Parameters.AddWithValue("@t", m.Title);
            cmd.Parameters.AddWithValue("@l", m.Location);
            cmd.Parameters.AddWithValue("@d", m.Description);
            cmd.Parameters.AddWithValue("@dt", m.Date.Date);
            cmd.Parameters.AddWithValue("@st", m.StartTime);
            cmd.Parameters.AddWithValue("@et", m.EndTime);
            cmd.Parameters.AddWithValue("@sub", DateTime.Now);
            cmd.ExecuteNonQuery();
        }

        /* GET all events for calendar */
        public IEnumerable<CalendarEventModel> GetCalendarEvents()
        {
            var list = new List<CalendarEventModel>();
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();

            var cmd = new MySqlCommand(@"
SELECT id,title,location,description,event_date,start_time,end_time
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
                    EndTime = rdr.GetTimeSpan("end_time")
                });
            }
            return list;
        }

        /* GET one event (for editing) */
        public CalendarEventModel GetCalendarEventById(int id)
        {
            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();

            var cmd = new MySqlCommand(@"
SELECT id,title,location,description,event_date,start_time,end_time
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
                EndTime = rdr.GetTimeSpan("end_time")
            };
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
    }
}
