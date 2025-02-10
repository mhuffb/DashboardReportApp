using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace DashboardReportApp.Services
{
    public class PressSetupService
    {
        private readonly string _connectionString = "server=192.168.1.6;database=sintergy;user=admin;password=N0mad2019";

        public List<PressSetupModel> GetAllRecords(string part, string operatorName, string machine, string setupComplete,
                                                   string assistanceRequired, string search, string startDate,
                                                   string endDate, string sortBy, string sortOrder)
        {
            var records = new List<PressSetupModel>();
            string query = "SELECT * FROM presssetup WHERE 1=1 ";

            var parameters = new List<MySqlParameter>();

            // Filters
            if (!string.IsNullOrEmpty(part))
            {
                query += " AND part LIKE @part ";
                parameters.Add(new MySqlParameter("@part", $"%{part}%"));
            }
            if (!string.IsNullOrEmpty(operatorName))
            {
                query += " AND operator = @operatorName ";
                parameters.Add(new MySqlParameter("@operatorName", operatorName));
            }
            if (!string.IsNullOrEmpty(machine))
            {
                query += " AND machine = @machine ";
                parameters.Add(new MySqlParameter("@machine", machine));
            }
            if (!string.IsNullOrEmpty(setupComplete))
            {
                query += " AND setupComp = @setupComplete ";
                parameters.Add(new MySqlParameter("@setupComplete", setupComplete));
            }
            if (!string.IsNullOrEmpty(assistanceRequired))
            {
                query += " AND assistanceReq = @assistanceRequired ";
                parameters.Add(new MySqlParameter("@assistanceRequired", assistanceRequired));
            }
            if (!string.IsNullOrEmpty(startDate))
            {
                query += " AND startDateTime >= @startDate ";
                parameters.Add(new MySqlParameter("@startDate", DateTime.Parse(startDate)));
            }
            if (!string.IsNullOrEmpty(endDate))
            {
                query += " AND startDateTime <= @endDate ";
                parameters.Add(new MySqlParameter("@endDate", DateTime.Parse(endDate)));
            }

            // Search across multiple fields
            if (!string.IsNullOrEmpty(search))
            {
                query += " AND (part LIKE @search OR operator LIKE @search OR machine LIKE @search OR notes LIKE @search)";
                parameters.Add(new MySqlParameter("@search", $"%{search}%"));
            }

            // Sorting
            string orderByColumn = "startDateTime"; // Default sorting
            switch (sortBy)
            {
                case "Part": orderByColumn = "part"; break;
                case "Operator": orderByColumn = "operator"; break;
                case "Machine": orderByColumn = "machine"; break;
                case "StartDateTime": orderByColumn = "startDateTime"; break;
                case "EndDateTime": orderByColumn = "endDateTime"; break;
            }

            query += $" ORDER BY {orderByColumn} {sortOrder}";

            using (var connection = new MySqlConnection(_connectionString))
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddRange(parameters.ToArray());

                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(new PressSetupModel
                        {
                            Id = Convert.ToInt32(reader["id"]),
                            Timestamp = reader["timestamp"] as DateTime?,
                            Part = reader["part"].ToString(),
                            Operator = reader["operator"].ToString(),
                            StartDateTime = reader["startDateTime"] as DateTime?,
                            EndDateTime = reader["endDateTime"] as DateTime?,
                            Machine = reader["machine"].ToString(),
                            PressType = reader["pressType"].ToString(),
                            Difficulty = reader["difficulty"].ToString(),
                            SetupComp = reader["setupComp"].ToString(),
                            AssistanceReq = reader["assistanceReq"].ToString(),
                            AssistedBy = reader["assistedBy"].ToString(),
                            Notes = reader["notes"].ToString()
                        });
                    }
                }
            }
            return records;
        }

        public async Task LoginAsync(string partNumber, string runNumber, string operatorName, string machine)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"INSERT INTO presssetup (part, run, operator, machine, startDateTime) 
                         VALUES (@part, @run, @operator, @machine, @startDateTime)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@part", partNumber.ToUpper());
                    command.Parameters.AddWithValue("@run", runNumber);
                    command.Parameters.AddWithValue("@operator", operatorName);
                    command.Parameters.AddWithValue("@machine", machine);
                    command.Parameters.AddWithValue("@startDateTime", DateTime.Now);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }


        public async Task LogoutAsync(string partNumber, DateTime startDateTime, string difficulty, string assistanceRequired,
                                      string assistedBy, string setupComplete, string notes)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"UPDATE presssetup 
                                 SET endDateTime = @endDateTime, 
                                     difficulty = @difficulty, 
                                     assistanceReq = @assistanceReq, 
                                     assistedBy = @assistedBy, 
                                     setupComp = @setupComp, 
                                     notes = @notes 
                                 WHERE part = @part AND startDateTime = @startDateTime";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@endDateTime", DateTime.Now);
                    command.Parameters.AddWithValue("@difficulty", difficulty);
                    command.Parameters.AddWithValue("@assistanceReq", assistanceRequired);
                    command.Parameters.AddWithValue("@assistedBy", assistanceRequired == "Assisted" ? assistedBy : null);
                    command.Parameters.AddWithValue("@setupComp", setupComplete);
                    command.Parameters.AddWithValue("@notes", notes);
                    command.Parameters.AddWithValue("@part", partNumber);
                    command.Parameters.AddWithValue("@startDateTime", startDateTime);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }
        public List<string> GetOperators()
        {
            var operators = new List<string>();
            string query = "SELECT name FROM operators WHERE dept = 'molding' ORDER BY name";

            using (var connection = new MySqlConnection(_connectionString))
            using (var command = new MySqlCommand(query, connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        operators.Add(reader["name"].ToString());
                    }
                }
            }

            return operators;
        }

        public List<string> GetEquipment()
        {
            var equipment = new List<string>();
            string query = "SELECT equipment FROM equipment WHERE department = 'molding' ORDER BY equipment";

            using (var connection = new MySqlConnection(_connectionString))
            using (var command = new MySqlCommand(query, connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        equipment.Add(reader["equipment"].ToString());
                    }
                }
            }

            return equipment;
        }

        public List<string> GetTrainers()
        {
            var trainers = new List<string>();
            string query = "SELECT name FROM operators WHERE dept = 'molding' AND level = 'trainer' ORDER BY name";

            using (var connection = new MySqlConnection(_connectionString))
            using (var command = new MySqlCommand(query, connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        trainers.Add(reader["name"].ToString());
                    }
                }
            }

            return trainers;
        }
        public string GetRunForPart(string part)
        {
            string run = "";
            string query = "SELECT run FROM schedule WHERE part = @part AND open = 1";

            using (var connection = new MySqlConnection(_connectionString))
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@part", part);
                connection.Open();
                var result = command.ExecuteScalar();
                if (result != null)
                {
                    run = result.ToString();
                }
            }
            return run;
        }
        public List<string> GetScheduledParts()
        {
            var parts = new List<string>();
            string query = "SELECT part FROM schedule WHERE open = '1' ORDER BY part";

            using (var connection = new MySqlConnection(_connectionString))
            using (var command = new MySqlCommand(query, connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        parts.Add(reader["part"].ToString());
                    }
                }
            }

            return parts;
        }
    }
}
