using MySql.Data.MySqlClient;

namespace DashboardReportApp.Services
{
    public interface ISecondarySetupLogService
    {
        Task<List<string>> GetEquipmentAsync();
        Task<List<string>> GetOperatorsAsync();
        Task<List<dynamic>> GetOpenSetupsAsync();
        Task AddSetupAsync(string op, string part, string machine, string run, int? pcs, int? scrapMach, int? scrapNonMach, string notes, decimal setupHours);
        Task<string> LookupPartNumberAsync(int? run);
    }

    public class SecondarySetupLogService : ISecondarySetupLogService
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

        public async Task<List<dynamic>> GetOpenSetupsAsync()
        {
            var setups = new List<dynamic>();
            string query = "SELECT part, machine, startDateTime, operator FROM secondarysetup WHERE endDateTime IS NULL";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        setups.Add(new
                        {
                            Part = reader["part"],
                            Machine = reader["machine"],
                            StartDateTime = reader["startDateTime"],
                            Operator = reader["operator"]
                        });
                    }
                }
            }

            return setups;
        }

        public async Task AddSetupAsync(string op, string part, string machine, string run, int? pcs, int? scrapMach, int? scrapNonMach, string notes, decimal setupHours)
        {
            string query = "INSERT INTO secondarysetup (operator, part, machine, run, pcs, scrapMach, scrapNonMach, notes, setupHours) " +
                           "VALUES (@op, @part, @machine, @run, @pcs, @scrapMach, @scrapNonMach, @notes, @setupHours)";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@op", op);
                    command.Parameters.AddWithValue("@part", part);
                    command.Parameters.AddWithValue("@machine", machine);
                    command.Parameters.AddWithValue("@run", run);
                    command.Parameters.AddWithValue("@pcs", pcs);
                    command.Parameters.AddWithValue("@scrapMach", scrapMach);
                    command.Parameters.AddWithValue("@scrapNonMach", scrapNonMach);
                    command.Parameters.AddWithValue("@notes", notes);
                    command.Parameters.AddWithValue("@setupHours", setupHours);

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
