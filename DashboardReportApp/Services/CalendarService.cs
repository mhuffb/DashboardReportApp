using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System.Data.Odbc;

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

        public List<CalendarModel> GetEmployees()
        {
            var employees = new List<CalendarModel>();
            using var connection = new OdbcConnection(_dataflexConnection);
            connection.Open();
            var command = new OdbcCommand("SELECT fname, lname, date_employed, active_status, email, vac_balance FROM employee where active_status = 'A'", connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                employees.Add(new CalendarModel
                {
                    FirstName = reader["fname"].ToString(),
                    LastName = reader["lname"].ToString(),
                    DateEmployed = Convert.ToDateTime(reader["date_employed"]),
                    ActiveStatus = reader["active_status"].ToString(),
                    Email = reader["email"].ToString(),
                    VacationBalance = Convert.ToDecimal(reader["vac_balance"])
                });
            }
            return employees;
        }

        public void SaveServiceRecord(CalendarModel model)
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

            /* trim first/last one more time */
            cmd.Parameters.AddWithValue("@fn", model.FirstName?.Trim());
            cmd.Parameters.AddWithValue("@ln", model.LastName?.Trim());
            cmd.Parameters.AddWithValue("@de", model.DateEmployed);
            cmd.Parameters.AddWithValue("@as", model.ActiveStatus);
            cmd.Parameters.AddWithValue("@em", model.Email);
            cmd.Parameters.AddWithValue("@vb", model.VacationBalance);
            cmd.Parameters.AddWithValue("@dept", model.Department);
            cmd.Parameters.AddWithValue("@shift", model.Shift);
            cmd.Parameters.AddWithValue("@sched", model.Schedule);
            cmd.Parameters.AddWithValue("@attr", model.Attribute);
            cmd.Parameters.AddWithValue("@expl", model.Explanation);
            cmd.Parameters.AddWithValue("@type", model.TimeOffType);
            cmd.Parameters.AddWithValue("@sub", model.SubmittedOn);

            int newId = Convert.ToInt32(cmd.ExecuteScalar());

            /* save each requested date */
            foreach (var d in model.DatesRequested)
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
SELECT sr.id, sr.status,
       sr.fname, sr.lname,
       sr.vac_balance,              -- ← add
       sr.department, sr.attribute,
       sr.time_off_type,
       d.requested_date
FROM servicerecords sr
JOIN servicerecord_dates d ON sr.id = d.servicerecord_id
ORDER BY sr.id, d.requested_date";


            using var conn = new MySqlConnection(_mysqlConnection);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            using var rdr = cmd.ExecuteReader();

            CalendarModel cur = null;
            int last = -1;
            while (rdr.Read())
            {
                int id = rdr.GetInt32("id");
                if (id != last)
                {
                    cur = new CalendarModel
                    {
                        Id = id,
                        Status = rdr["status"].ToString(),
                        FirstName = rdr["fname"].ToString(),
                        LastName = rdr["lname"].ToString(),
                        VacationBalance = Convert.ToDecimal(rdr["vac_balance"]),   // ← new line
                        Department = rdr["department"].ToString(),
                        Attribute = rdr["attribute"].ToString(),
                        TimeOffType = rdr["time_off_type"].ToString(),
                        DatesRequested = new List<DateTime>()
                    };

                    results.Add(cur);
                    last = id;
                }
                cur.DatesRequested.Add(Convert.ToDateTime(rdr["requested_date"]));
            }
            return results;
        }


    }

}
