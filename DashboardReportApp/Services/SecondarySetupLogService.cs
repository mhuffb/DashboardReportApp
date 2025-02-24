using MySql.Data.MySqlClient;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Threading.Tasks;
using DashboardReportApp.Models;
using System.Data;

namespace DashboardReportApp.Services
{
    public class SecondarySetupLogService
    {
        private readonly string _connectionStringMySQL;

        public SecondarySetupLogService(IConfiguration configuration)
        {
            _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
        }

        public async Task<List<string>> GetEquipmentAsync()
        {
            var equipment = new List<string>();
            string query = "SELECT equipment FROM equipment WHERE department = 'secondary' ORDER BY equipment";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        equipment.Add(reader["equipment"].ToString());
                    }
                }
            }
            return equipment;
        }

        public async Task<List<string>> GetOperatorsAsync()
        {
            var operators = new List<string>();
            string query = "SELECT name FROM operators WHERE dept = 'secondary' ORDER BY name";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        operators.Add(reader["name"].ToString());
                    }
                }
            }
            return operators;
        }

        public async Task<List<SecondarySetupLogModel>> GetAllRecords()
        {
            var setups = new List<SecondarySetupLogModel>();

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                string query = "Select * from secondarysetup order by id desc";
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        // Map to your model's properties
                        var record = new SecondarySetupLogModel
                        {
                            Id = Convert.ToInt32(reader["id"]),
                            ProdNumber = reader["prodNumber"].ToString(),
                            Run = reader.IsDBNull(reader.GetOrdinal("run"))
    ? 0
    : int.TryParse(reader["run"]?.ToString(), out int runValue)
        ? runValue
        : 0,

                            Part = reader["part"].ToString(),
                            Operator = reader["operator"].ToString(),
                            Op = reader["op"].ToString(),
                            Machine = reader["machine"].ToString(),
                            Pcs = reader["pcs"] as int?,
                            ScrapMach = reader["scrapMach"] as int?,
                            ScrapNonMach = reader["scrapNonMach"] as int?,
                            SetupHours = reader["setupHours"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["setupHours"]),
                            Notes = reader["notes"].ToString(),
                            Timestamp = reader.IsDBNull(reader.GetOrdinal("timestamp")) ? default(DateTime) : reader.GetDateTime("timestamp"),


                        };
                        setups.Add(record);
                    }
                }
            }

            return setups;
        }


        public async Task AddSetupAsync(SecondarySetupLogModel model)
        {
            string query = "INSERT INTO secondarysetup (operator, op, part, machine, run, pcs, scrapMach, scrapNonMach, notes, setupHours) " +
                           "VALUES (@operator, @op, @part, @machine, @run, @pcs, @scrapMach, @scrapNonMach, @notes, @setupHours)";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@operator", model.Operator);
                    command.Parameters.AddWithValue("@op", model.Op);
                    command.Parameters.AddWithValue("@part", model.Part);
                    command.Parameters.AddWithValue("@machine", model.Machine);
                    command.Parameters.AddWithValue("@run", model.Run);
                    command.Parameters.AddWithValue("@pcs", model.Pcs);
                    command.Parameters.AddWithValue("@scrapMach", model.ScrapMach);
                    command.Parameters.AddWithValue("@scrapNonMach", model.ScrapNonMach);
                    command.Parameters.AddWithValue("@notes", model.Notes);
                    command.Parameters.AddWithValue("@setupHours", model.SetupHours);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task<string> LookupPartNumberAsync(int? run)
        {
            string query = "SELECT part FROM schedule WHERE run = @run";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@run", run);
                    return (await command.ExecuteScalarAsync())?.ToString();
                }
            }
        }
    }
}
