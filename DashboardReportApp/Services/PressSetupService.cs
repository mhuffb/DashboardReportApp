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

        public async Task<List<PressSetupModel>> GetAllPressSetupRecordsAsync()
        {
            var records = new List<PressSetupModel>();
            string query = "SELECT * FROM presssetup ORDER BY startDateTime DESC";

            using (var connection = new MySqlConnection(_connectionString))
            using (var command = new MySqlCommand(query, connection))
            {
                await connection.OpenAsync();
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        records.Add(new PressSetupModel
                        {
                            Id = reader.GetInt32("id"),
                            Timestamp = reader["timestamp"] as DateTime?,
                            Part = reader["part"].ToString(),
                            Operator = reader["operator"].ToString(),
                            StartDateTime = reader["startDateTime"] as DateTime?,
                            EndDateTime = reader["endDateTime"] as DateTime?,
                            Machine = reader["machine"].ToString(),
                            PressType = reader["pressType"]?.ToString(),
                            Difficulty = reader["difficulty"]?.ToString(),
                            SetupComp = reader["setupComp"]?.ToString(),
                            AssistanceReq = reader["assistanceReq"]?.ToString(),
                            AssistedBy = reader["assistedBy"]?.ToString(),
                            Notes = reader["notes"]?.ToString()
                        });
                    }
                }
            }

            return records;
        }

        public async Task AddLoginAsync(PressSetupModel setup)
        {
            string query = @"INSERT INTO presssetup (part, operator, machine, startDateTime) 
                             VALUES (@part, @operator, @machine, @startDateTime)";

            using (var connection = new MySqlConnection(_connectionString))
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@part", setup.Part?.ToUpper() ?? "UNKNOWN");
                command.Parameters.AddWithValue("@operator", setup.Operator ?? "UNKNOWN");
                command.Parameters.AddWithValue("@machine", setup.Machine ?? "UNKNOWN");
                command.Parameters.AddWithValue("@startDateTime", setup.StartDateTime);

                await connection.OpenAsync();
                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task UpdateLogoutAsync(PressSetupModel setup)
        {
            string query = @"UPDATE presssetup 
                             SET endDateTime = @endDateTime, 
                                 difficulty = @difficulty, 
                                 assistanceReq = @assistanceReq, 
                                 assistedBy = @assistedBy, 
                                 setupComp = @setupComp, 
                                 notes = @notes 
                             WHERE part = @part AND startDateTime = @startDateTime";

            using (var connection = new MySqlConnection(_connectionString))
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@endDateTime", DateTime.Now);
                command.Parameters.AddWithValue("@difficulty", setup.Difficulty ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@assistanceReq", setup.AssistanceReq ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@assistedBy", setup.AssistedBy ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@setupComp", setup.SetupComp ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@notes", setup.Notes ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@part", setup.Part);
                command.Parameters.AddWithValue("@startDateTime", setup.StartDateTime);

                await connection.OpenAsync();
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
