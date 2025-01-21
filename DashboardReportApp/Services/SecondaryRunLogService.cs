using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DashboardReportApp.Services
{
    public interface ISecondaryRunLogService
    {
        Task LoginAsync(string operatorName, string machine, string runNumber, string op); // For CreateRun
        Task LoginAsync(string operatorName, string machine, string runNumber); // For Login
        Task LogoutAsync(int pcs, int scrapMach, int scrapNonMach, string notes, int selectedRunId);
        Task<IEnumerable<SecondaryRunLogViewModel>> GetActiveRunsAsync();
        Task<SecondaryRunLogViewModel> GetRunByIdAsync(int id);
        Task<IEnumerable<string>> GetOperatorsAsync();
        Task<IEnumerable<string>> GetMachinesAsync();
    }

    public class SecondaryRunLogService : ISecondaryRunLogService
    {
        private const string ConnectionString = "server=192.168.1.6;database=sintergy;user=admin;password=N0mad2019";
        private const string UpdateTable = "secondaryrun";
        public async Task<IEnumerable<string>> GetOperatorsAsync()
        {
            var operators = new List<string>();
            string query = "SELECT name FROM operators WHERE dept = 'secondary' ORDER BY name";

            using (var connection = new MySqlConnection(ConnectionString))
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

            using (var connection = new MySqlConnection(ConnectionString))
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
        public async Task<IEnumerable<SecondaryRunLogViewModel>> GetActiveRunsAsync()
        {
            var activeRuns = new List<SecondaryRunLogViewModel>();
            string query = $"SELECT id, timestamp, run, part, op, operator, startDateTime, machine, notes FROM secondaryrun WHERE endDateTime IS NULL";

            using (var connection = new MySqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            activeRuns.Add(new SecondaryRunLogViewModel
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                Timestamp = Convert.ToDateTime(reader["timestamp"]),
                                Run = reader["run"].ToString(),
                                Part = reader["part"].ToString(),
                                Op = reader["op"] != DBNull.Value ? reader["op"].ToString() : null,
                                Operator = reader["operator"].ToString(),
                                StartDateTime = Convert.ToDateTime(reader["startDateTime"]),
                                Machine = reader["machine"].ToString(),
                                Notes = reader["notes"] != DBNull.Value ? reader["notes"].ToString() : null
                            });
                        }
                    }
                }
            }
            return activeRuns;
        }


        public async Task LoginAsync(string operatorName, string machine, string runNumber, string op)
        {
            string part = await LookupPartNumberAsync(runNumber); // Use helper method to fetch the part

            string query = $@"INSERT INTO secondaryrun 
                      (run, operator, machine, op, part, startDateTime) 
                      VALUES (@run, @operator, @machine, @op, @part, @startDateTime)";

            using (var connection = new MySqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@run", runNumber);
                    command.Parameters.AddWithValue("@operator", operatorName);
                    command.Parameters.AddWithValue("@machine", machine);
                    command.Parameters.AddWithValue("@op", string.IsNullOrEmpty(op) ? DBNull.Value : op);
                    command.Parameters.AddWithValue("@part", string.IsNullOrEmpty(part) ? DBNull.Value : part);
                    command.Parameters.AddWithValue("@startDateTime", DateTime.Now);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task<string> LookupPartNumberAsync(string runNumber)
        {
            string query = "SELECT part FROM schedule WHERE run = @run";
            string part = null;

            using (var connection = new MySqlConnection(ConnectionString))
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


        // Overload for Login
        public async Task LoginAsync(string operatorName, string machine, string runNumber)
        {
            string query = $"INSERT INTO secondaryrun (run, operator, machine, startDateTime) VALUES (@run, @operator, @machine, @startDateTime)";

            using (var connection = new MySqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@run", runNumber);
                    command.Parameters.AddWithValue("@operator", operatorName);
                    command.Parameters.AddWithValue("@machine", machine);
                    command.Parameters.AddWithValue("@startDateTime", DateTime.Now);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task LogoutAsync(int pcs, int scrapMach, int scrapNonMach, string notes, int selectedRunId)
        {
            string query = $"UPDATE {UpdateTable} SET pcs = @pcs, scrapMach = @scrapMach, scrapNonMach = @scrapNonMach, notes = @notes, endDateTime = @endDateTime WHERE id = @id";

            using (var connection = new MySqlConnection(ConnectionString))
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
        public async Task<SecondaryRunLogViewModel> GetRunByIdAsync(int id)
        {
            string query = $"SELECT * FROM secondaryrun WHERE id = @id";
            SecondaryRunLogViewModel run = null;

            using (var connection = new MySqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@id", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            run = new SecondaryRunLogViewModel
                            {
                                Id = id,
                                Part = reader["part"].ToString(),
                                Machine = reader["machine"].ToString(),
                                StartDateTime = Convert.ToDateTime(reader["startDateTime"]),
                                Operator = reader["operator"].ToString()
                            };
                        }
                    }
                }
            }
            return run;
        }
    }
}
