namespace DashboardReportApp.Services
{
    using MySql.Data.MySqlClient;
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

        public async Task<Dictionary<string, string>> GetOpenPartsWithRunsAsync()
        {
            var partsWithRuns = new Dictionary<string, string>();

            const string query = "SELECT part, run FROM presssetup WHERE open = 1 ORDER BY part";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var part = reader["part"].ToString();
                var run = reader["run"].ToString();

                if (!string.IsNullOrEmpty(part) && !partsWithRuns.ContainsKey(part))
                {
                    partsWithRuns[part] = run;
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
     string notes
 )
        {
            const string query = @"INSERT INTO presslotchange (part, sentDateTime, operator, machine, lotNumber, mixNumber, notes, supplierItemNumber) 
                           VALUES (@part, @sentDateTime, @operator, @machine, @lotNumber, @mixNumber, @notes, @supplierItemNumber)";

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

    }
}
