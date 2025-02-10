namespace DashboardReportApp.Services
{
    using DashboardReportApp.Models;
    using MySql.Data.MySqlClient;
    using System.Data;

    public class PressMixBagChangeService
    {
        private readonly string _connectionStringMySQL;
        //private readonly string _connectionStringDataflex;

        public PressMixBagChangeService(IConfiguration configuration)
        {
            _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
        }

        public async Task<List<string>> GetEquipmentAsync()
        {
            var equipmentList = new List<string>();
            const string query = "SELECT equipment FROM equipment WHERE department = 'molding' ORDER BY equipment";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                equipmentList.Add(reader["equipment"].ToString());
            }

            return equipmentList;
        }

        public async Task<List<string>> GetOperatorsAsync()
        {
            var operatorList = new List<string>();
            const string query = "SELECT name FROM operators WHERE dept = 'molding' ORDER BY name";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                operatorList.Add(reader["name"].ToString());
            }

            return operatorList;
        }

        public async Task<List<PressSetupModel>> GetOpenPartsWithRunsAsync()
        {
            var partsWithRuns = new List<PressSetupModel>();

            const string query = @"
        SELECT part, run, operator, machine 
        FROM presssetup 
        WHERE open = 1 
        ORDER BY part, run";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var part = reader["part"].ToString();
                var run = reader["run"].ToString();
                var operatorName = reader["operator"]?.ToString() ?? "N/A";
                var machine = reader["machine"]?.ToString() ?? "N/A";

                if (!string.IsNullOrEmpty(part) && !string.IsNullOrEmpty(run))
                {
                    partsWithRuns.Add(new PressSetupModel
                    {
                        Part = part,
                        Run = run,
                        Operator = operatorName,
                        Machine = machine
                    });
                }
            }

            return partsWithRuns;
        }




        public async Task InsertPressLotChangeAsync(
    string part,
    string time,
    string op,
    string machine,
    string lot,
    string mix,
    string notes)
        {
            Console.WriteLine($"Inserting: Part={part}, Time={time}, Operator={op}, Machine={machine}, Lot={lot}, Mix={mix}, Notes={notes}");

            const string query = @"INSERT INTO presslotchange (part, sentDateTime, operator, machine, lotNumber, mixNumber, notes) 
                           VALUES (@part, @sentDateTime, @operator, @machine, @lotNumber, @mixNumber, @notes)";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@part", part);
            command.Parameters.AddWithValue("@sentDateTime", time);
            command.Parameters.AddWithValue("@operator", op);
            command.Parameters.AddWithValue("@machine", machine);
            command.Parameters.AddWithValue("@lotNumber", lot);
            command.Parameters.AddWithValue("@mixNumber", mix);
            command.Parameters.AddWithValue("@notes", notes);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<List<PressMixBagChangeModel>> GetAllMixBagChangesAsync()
        {
            var records = new List<PressMixBagChangeModel>();

            const string query = @"
        SELECT id, part, run, operator, machine, lotNumber, mixNumber, sentDateTime, notes
        FROM presslotchange
        ORDER BY sentDateTime DESC";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                records.Add(new PressMixBagChangeModel
                {
                    Id = reader.GetInt32("id"),
                    Part = reader["part"]?.ToString() ?? "N/A",
                    Run = reader["run"]?.ToString() ?? "N/A",
                    Operator = reader["operator"]?.ToString() ?? "N/A",
                    Machine = reader["machine"]?.ToString() ?? "N/A",
                    LotNumber = reader["lotNumber"]?.ToString() ?? "N/A",
                    MixNumber = reader["mixNumber"]?.ToString() ?? "N/A",
                    SentDateTime = reader.GetDateTime("sentDateTime"),
                    Notes = reader["notes"]?.ToString() ?? "N/A"
                });
            }

            return records;
        }
    }
    

    }
