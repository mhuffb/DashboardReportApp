using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace DashboardReportApp.Services
{
    public class SecondaryRunLogService
    {
        private const string _connectionStringMySQL = "server=192.168.1.6;database=sintergy;user=admin;password=N0mad2019";
        private const string UpdateTable = "secondaryrun";

        public async Task<List<SecondaryRunLogModel>> GetAllRunsAsync()
        {
            var allRuns = new List<SecondaryRunLogModel>();

            const string query = @"
                SELECT id, timestamp, run, part, machine, operator, op, pcs, scrapMach, scrapNonMach, startDateTime, endDateTime, notes, appearance
                FROM secondaryrun
                ORDER BY id DESC";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                allRuns.Add(new SecondaryRunLogModel
                {
                    Id = reader.IsDBNull(reader.GetOrdinal("id")) ? 0 : reader.GetInt32("id"),
                    Timestamp = reader.IsDBNull(reader.GetOrdinal("timestamp")) ? default(DateTime) : reader.GetDateTime("timestamp"),
                    Run = reader["run"]?.ToString() ?? "N/A",
                    Part = reader["part"]?.ToString() ?? "N/A",
                    Machine = reader["machine"]?.ToString(),
                    Operator = reader["operator"]?.ToString(),
                    Op = reader["op"]?.ToString(),
                    Pcs = reader.IsDBNull(reader.GetOrdinal("pcs")) ? 0 : reader.GetInt32("pcs"),
                    ScrapMach = reader.IsDBNull(reader.GetOrdinal("scrapMach")) ? 0 : reader.GetInt32("scrapMach"),
                    ScrapNonMach = reader.IsDBNull(reader.GetOrdinal("scrapNonMach")) ? 0 : reader.GetInt32("scrapNonMach"),
                    StartDateTime = reader.IsDBNull(reader.GetOrdinal("startDateTime")) ? default(DateTime) : reader.GetDateTime("startDateTime"),
                    EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime")) ? (DateTime?)null : reader.GetDateTime("endDateTime"),
                    Notes = reader["notes"]?.ToString(),
                    Appearance = reader["appearance"]?.ToString(),
                });
            }

            return allRuns;
        }

        public async Task<List<SecondaryRunLogModel>> GetLoggedInRunsAsync()
        {
            var loggedInRuns = new List<SecondaryRunLogModel>();

            const string query = @"
                SELECT id, timestamp, run, part, machine, operator, op, pcs, scrapMach, scrapNonMach, startDateTime, endDateTime, notes, appearance
                FROM secondaryrun
                WHERE open = 1";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                loggedInRuns.Add(new SecondaryRunLogModel
                {
                    Id = reader.IsDBNull(reader.GetOrdinal("id")) ? 0 : reader.GetInt32("id"),
                    Timestamp = reader.IsDBNull(reader.GetOrdinal("timestamp")) ? default(DateTime) : reader.GetDateTime("timestamp"),
                    Run = reader["run"]?.ToString() ?? "N/A",
                    Part = reader["part"]?.ToString() ?? "N/A",
                    Machine = reader["machine"]?.ToString(),
                    Operator = reader["operator"]?.ToString(),
                    Op = reader["op"]?.ToString(),
                    Pcs = reader.IsDBNull(reader.GetOrdinal("pcs")) ? 0 : reader.GetInt32("pcs"),
                    ScrapMach = reader.IsDBNull(reader.GetOrdinal("scrapMach")) ? 0 : reader.GetInt32("scrapMach"),
                    ScrapNonMach = reader.IsDBNull(reader.GetOrdinal("scrapNonMach")) ? 0 : reader.GetInt32("scrapNonMach"),
                    StartDateTime = reader.IsDBNull(reader.GetOrdinal("startDateTime")) ? default(DateTime) : reader.GetDateTime("startDateTime"),
                    EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime")) ? (DateTime?)null : reader.GetDateTime("endDateTime"),
                    Notes = reader["notes"]?.ToString(),
                    Appearance = reader["appearance"]?.ToString(),
                });
            }

            return loggedInRuns;
        }

        public async Task<IEnumerable<string>> GetOperatorsAsync()
        {
            var operators = new List<string>();
            string query = "SELECT name FROM operators WHERE dept = 'secondary' ORDER BY name";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            operators.Add(reader["name"].ToString());
                        }
                    }
                }
            }

            return operators;
        }

        public async Task<IEnumerable<string>> GetMachinesAsync()
        {
            var machines = new List<string>();
            string query = "SELECT equipment FROM equipment WHERE department = 'secondary' ORDER BY equipment";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            machines.Add(reader["equipment"].ToString());
                        }
                    }
                }
            }

            return machines;
        }

       
        public async Task HandleLoginAsync(string operatorName, string machine, string runNumber, string op)
        {
            string part = await LookupPartNumberAsync(runNumber); // Use helper method to fetch the part

            string query = @"INSERT INTO secondaryrun 
                      (run, operator, machine, op, part, startDateTime, open) 
                      VALUES (@run, @operator, @machine, @op, @part, @startDateTime, 1)";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@run", runNumber);
                    command.Parameters.AddWithValue("@operator", operatorName);
                    command.Parameters.AddWithValue("@machine", machine);
                    command.Parameters.AddWithValue("@op", string.IsNullOrEmpty(op) ? DBNull.Value : (object)op);
                    command.Parameters.AddWithValue("@part", string.IsNullOrEmpty(part) ? DBNull.Value : (object)part);
                    command.Parameters.AddWithValue("@startDateTime", DateTime.Now);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task<string> LookupPartNumberAsync(string runNumber)
        {
            string query = "SELECT part FROM schedule WHERE run = @run";
            string part = null;

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@run", runNumber);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            part = reader["part"]?.ToString();
                        }
                    }
                }
            }
            return part;
        }

      
        public async Task HandleLogoutAsync(int pcs, int scrapMach, int scrapNonMach, string notes, int selectedRunId)
        {
            string query = $"UPDATE {UpdateTable} SET pcs = @pcs, scrapMach = @scrapMach, scrapNonMach = @scrapNonMach, notes = @notes, endDateTime = @endDateTime, open = 0 WHERE id = @id";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@pcs", pcs);
                    command.Parameters.AddWithValue("@scrapMach", scrapMach);
                    command.Parameters.AddWithValue("@scrapNonMach", scrapNonMach);
                    command.Parameters.AddWithValue("@notes", notes);
                    command.Parameters.AddWithValue("@endDateTime", DateTime.Now);
                    command.Parameters.AddWithValue("@id", selectedRunId);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

       
    }
}
