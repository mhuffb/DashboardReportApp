using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DashboardReportApp.Services
{
    public class SecondaryRunLogService
    {
        private readonly string _connectionStringMySQL;
        private const string UpdateTable = "secondaryrun";
        public SecondaryRunLogService(IConfiguration configuration)
        {
            _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
        }
        public async Task<List<SecondaryRunLogModel>> GetAllRunsAsync()
        {
            var allRuns = new List<SecondaryRunLogModel>();

            const string query = @"
                SELECT id, prodNumber, run, part, machine, operator, op, pcs, scrapMach, scrapNonMach, startDateTime, endDateTime, notes, appearance
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
                    ProdNumber = reader["prodNumber"]?.ToString() ?? "N/A",
                    Run = reader.IsDBNull(reader.GetOrdinal("run"))
    ? 0
    : int.TryParse(reader["run"]?.ToString(), out int runValue)
        ? runValue
        : 0,

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
                SELECT id, date, prodNumber, run, part, machine, operator, op, pcs, scrapMach, scrapNonMach, startDateTime, endDateTime, notes, appearance
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
                    Date = reader.IsDBNull(reader.GetOrdinal("date")) ? default(DateTime) : reader.GetDateTime("date"),
                    ProdNumber = reader["prodNumber"]?.ToString() ?? "N/A",
                    Run = reader.IsDBNull(reader.GetOrdinal("run")) ? 0 : reader.GetInt32(reader.GetOrdinal("run")),
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
        public async Task<List<SecondarySetupLogModel>> GetAvailableParts()
        {
            var availableParts = new List<SecondarySetupLogModel>();

            string query = @"
            SELECT id, date, operator, prodNumber, run, op, part, machine, notes, open
            FROM secondarysetup WHERE open = 1" +
                    " ORDER BY id DESC";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                DateTime date = !reader.IsDBNull(reader.GetOrdinal("date"))
                                     ? reader.GetDateTime("date")
                                     : DateTime.MinValue;

                availableParts.Add(new SecondarySetupLogModel
                {
                    Id = reader.GetInt32("id"),
                    Date = date,
                    Operator = reader["operator"]?.ToString(),
                    ProdNumber = reader["prodNumber"]?.ToString() ?? "N/A",
                    Run = reader.GetInt32("run"),
                    Op = reader["op"]?.ToString() ?? "N/A",
                    Part = reader["part"]?.ToString() ?? "N/A",
                    Machine = reader["machine"]?.ToString(),
                    Notes = reader["notes"]?.ToString(),
                    Open = reader["open"] != DBNull.Value ? Convert.ToSByte(reader["open"]) : (sbyte)0
                });
            }

            return availableParts;
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

       
        public async Task HandleLoginAsync(SecondaryRunLogModel model)
        {

            string query = @"INSERT INTO secondaryrun 
                      (prodNumber, run, operator, machine, op, part, startDateTime, open) 
                      VALUES (@prodNumber, @run, @operator, @machine, @op, @part, @startDateTime, 1)";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                    command.Parameters.AddWithValue("@run", model.Run);
                    command.Parameters.AddWithValue("@operator", model.Operator);
                    command.Parameters.AddWithValue("@machine", model.Machine);
                    command.Parameters.AddWithValue("@op", string.IsNullOrEmpty(model.Op) ? DBNull.Value : (object)model.Op);
                    command.Parameters.AddWithValue("@part", string.IsNullOrEmpty(model.Part) ? DBNull.Value : (object)model.Part);
                    command.Parameters.AddWithValue("@startDateTime", DateTime.Now);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task<string> LookupPartNumberAsync(int runNumber)
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

        public async Task UpdateSecondarySetupAsync(string prodNumber, string part)
        {
            string query = "UPDATE secondarysetup SET open = 0 WHERE prodNumber = @prodNumber AND part = @part";
            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@prodNumber", prodNumber);
                    command.Parameters.AddWithValue("@part", part);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
        public async Task UpdateScheduleAsync(string prodNumber, string part, bool orderComplete)
        {
            Console.WriteLine(prodNumber + " " + part + " " + orderComplete);
            int openToSecondaryValue = orderComplete ? 0 : 1;
            string query = "UPDATE schedule SET openToSecondary = @openToSecondary WHERE part = @part AND prodNumber = @prodNumber";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@openToSecondary", openToSecondaryValue);
                    command.Parameters.AddWithValue("@prodNumber", prodNumber);
                    command.Parameters.AddWithValue("@part", part);

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    // Log the result (for example, using a logger or even Debug.WriteLine)
                    Debug.WriteLine($"UpdateScheduleAsync affected {rowsAffected} row(s) for part '{part}' and prodNumber '{prodNumber}'");
                }
            }
        }


    }
}
