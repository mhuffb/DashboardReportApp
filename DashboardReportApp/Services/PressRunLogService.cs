using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace DashboardReportApp.Services
{
    public class PressRunLogService
    {
        private readonly string _connectionStringMySQL;

        public PressRunLogService(IConfiguration configuration)
        {
            _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
        }

        /// <summary>
        /// Get all runs that are currently open (open=1).
        /// </summary>
        public async Task<List<PressRunLogModel>> GetLoggedInRunsAsync()
        {
            var loggedInRuns = new List<PressRunLogModel>();

            const string query = @"
                SELECT id, timestamp, run, part, startDateTime, endDateTime, operator, machine, pcsStart, pcsEnd, scrap, notes
                FROM pressrun
                WHERE open = 1";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                loggedInRuns.Add(new PressRunLogModel
                {
                    Id = reader.GetInt32("id"),
                    Timestamp = reader.GetDateTime("timestamp"),
                    Run = reader["run"]?.ToString() ?? "N/A",
                    Part = reader["part"]?.ToString() ?? "N/A",
                    StartDateTime = reader.GetDateTime("startDateTime"),
                    EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                                   ? null : reader.GetDateTime("endDateTime"),
                    Operator = reader["operator"]?.ToString(),
                    Machine = reader["machine"]?.ToString(),
                    PcsStart = reader.IsDBNull(reader.GetOrdinal("pcsStart")) ? 0 : reader.GetInt32("pcsStart"),
                    PcsEnd = reader.IsDBNull(reader.GetOrdinal("pcsEnd")) ? 0 : reader.GetInt32("pcsEnd"),
                    Scrap = reader.IsDBNull(reader.GetOrdinal("scrap")) ? 0 : reader.GetInt32("scrap"),
                    Notes = reader["notes"]?.ToString()
                });
            }

            return loggedInRuns;
        }

        /// <summary>
        /// Get *all* runs in descending order by startDateTime.
        /// </summary>
        public async Task<List<PressRunLogModel>> GetAllRunsAsync()
        {
            var allRuns = new List<PressRunLogModel>();

            const string query = @"
                SELECT id, timestamp, run, part, startDateTime, endDateTime, operator, machine, pcsStart, pcsEnd, scrap, notes
                FROM pressrun
                ORDER BY startDateTime DESC";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                allRuns.Add(new PressRunLogModel
                {
                    Id = reader.GetInt32("id"),
                    Timestamp = reader.GetDateTime("timestamp"),
                    Run = reader["run"]?.ToString() ?? "N/A",
                    Part = reader["part"]?.ToString() ?? "N/A",
                    StartDateTime = reader.GetDateTime("startDateTime"),
                    EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                                   ? null : reader.GetDateTime("endDateTime"),
                    Operator = reader["operator"]?.ToString(),
                    Machine = reader["machine"]?.ToString(),
                    PcsStart = reader.IsDBNull(reader.GetOrdinal("pcsStart")) ? 0 : reader.GetInt32("pcsStart"),
                    PcsEnd = reader.IsDBNull(reader.GetOrdinal("pcsEnd")) ? 0 : reader.GetInt32("pcsEnd"),
                    Scrap = reader.IsDBNull(reader.GetOrdinal("scrap")) ? 0 : reader.GetInt32("scrap"),
                    Notes = reader["notes"]?.ToString()
                });
            }

            return allRuns;
        }

        public async Task<List<string>> GetOperatorsAsync()
        {
            var operators = new List<string>();
            const string query = @"SELECT name FROM operators 
                                   WHERE dept = 'molding' 
                                   ORDER BY name";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                operators.Add(reader["name"].ToString());
            }

            return operators;
        }

        public async Task<List<string>> GetEquipmentAsync()
        {
            var equipment = new List<string>();
            const string query = @"SELECT equipment FROM equipment 
                                   WHERE name = 'press' 
                                     AND (department = 'molding' OR department = 'sizing') 
                                   ORDER BY equipment";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                equipment.Add(reader["equipment"]?.ToString());
            }

            return equipment;
        }

        public async Task<Dictionary<(string Part, string Run), string>> GetOpenPartsWithRunsAndMachinesAsync()
        {
            var partsWithDetails = new Dictionary<(string Part, string Run), string>();

            const string query = @"
                SELECT part, run, machine
                FROM presssetup
                WHERE open = 1
                ORDER BY part, run";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var part = reader["part"]?.ToString() ?? "N/A";
                var run = reader["run"]?.ToString() ?? "N/A";
                var machine = reader["machine"]?.ToString() ?? "N/A";

                if (!string.IsNullOrEmpty(part) && !string.IsNullOrEmpty(run))
                {
                    partsWithDetails[(part, run)] = machine;
                }
            }

            return partsWithDetails;
        }

        public async Task HandleLoginAsync(PressRunLogModel formModel)
        {
            const string query = @"
                INSERT INTO pressrun (operator, part, machine, startDateTime, open)
                VALUES (@operator, @part, @machine, @startDateTime, 1)";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);

            command.Parameters.AddWithValue("@operator", formModel.Operator);
            command.Parameters.AddWithValue("@part", formModel.Part);
            command.Parameters.AddWithValue("@machine", formModel.Machine);
            command.Parameters.AddWithValue("@startDateTime", formModel.StartDateTime);

            await command.ExecuteNonQueryAsync();
        }

        public async Task HandleLogoutAsync(PressRunLogModel formModel)
        {
            const string query = @"
                UPDATE pressrun
                SET endDateTime = @endDateTime, scrap = @scrap, notes = @notes, open = 0
                WHERE part = @part AND startDateTime = @startDateTime";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);

            command.Parameters.AddWithValue("@endDateTime", formModel.EndDateTime);
            command.Parameters.AddWithValue("@scrap", formModel.Scrap);
            command.Parameters.AddWithValue("@notes", formModel.Notes ?? string.Empty);
            command.Parameters.AddWithValue("@part", formModel.Part);
            command.Parameters.AddWithValue("@startDateTime", formModel.StartDateTime);

            await command.ExecuteNonQueryAsync();
        }
    }
}
