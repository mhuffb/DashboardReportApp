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
        SELECT part, component, prodNumber, run, operator, machine 
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
                var component = reader["component"].ToString();
                var prodNumber = reader["prodNumber"].ToString();
                var run = reader["run"].ToString();
                var operatorName = reader["operator"]?.ToString() ?? "N/A";
                var machine = reader["machine"]?.ToString() ?? "N/A";

                if (!string.IsNullOrEmpty(part) && !string.IsNullOrEmpty(run))
                {
                    partsWithRuns.Add(new PressSetupModel
                    {
                        Part = part,
                        Component = component,
                        ProdNumber = prodNumber,
                        Run = run,
                        Operator = operatorName,
                        Machine = machine
                    });
                }
            }

            return partsWithRuns;
        }




        public async Task InsertPressMixBagChangeAsync(
      string part,
      string component,
      string prodNumber,
      string run,
      string time,
      string op,
      string machine,
      string lot,
      string materialCode,
      decimal weight,
      string bagNumber,
      string notes,
      bool isOverride = false,
      string overrideBy = null,
      DateTime? overrideAt = null)
        {
            const string query = @"
    INSERT INTO pressmixbagchange 
    (part, component, prodNumber, run, sentDateTime, operator, machine, lotNumber, materialCode, weightLbs, bagNumber, notes, isOverride, overrideBy, overrideAt) 
    VALUES 
    (@part, @component, @prodNumber, @run, @sentDateTime, @operator, @machine, @lotNumber, @materialCode, @weight, @bagNumber, @notes, @isOverride, @overrideBy, @overrideAt)";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@part", part);
            command.Parameters.AddWithValue("@component", component);
            command.Parameters.AddWithValue("@prodNumber", prodNumber);
            command.Parameters.AddWithValue("@run", run);
            command.Parameters.AddWithValue("@sentDateTime", time);
            command.Parameters.AddWithValue("@operator", op);
            command.Parameters.AddWithValue("@machine", machine);
            command.Parameters.AddWithValue("@lotNumber", lot);
            command.Parameters.AddWithValue("@materialCode", materialCode);
            command.Parameters.AddWithValue("@weight", weight);
            command.Parameters.AddWithValue("@bagNumber", bagNumber);
            command.Parameters.AddWithValue("@notes", notes ?? "");
            command.Parameters.AddWithValue("@isOverride", isOverride);
            command.Parameters.AddWithValue("@overrideBy", (object?)overrideBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@overrideAt", (object?)overrideAt ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }




        public async Task<List<PressMixBagChangeModel>> GetAllMixBagChangesAsync()
        {
            var records = new List<PressMixBagChangeModel>();

            const string query = @"
        SELECT id,
               part, component, prodNumber, run,
               operator, machine,
               lotNumber, materialCode, weightLbs, bagNumber,
               sentDateTime, notes,
               isOverride, overrideBy, overrideAt
        FROM pressmixbagchange
        ORDER BY id DESC;";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var m = new PressMixBagChangeModel
                {
                    Id = reader.GetInt32("id"),
                    Part = reader["part"]?.ToString() ?? "",
                    Component = reader["component"]?.ToString() ?? "",
                    ProdNumber = reader["prodNumber"]?.ToString() ?? "",
                    Run = reader["run"]?.ToString() ?? "",
                    Operator = reader["operator"]?.ToString() ?? "",
                    Machine = reader["machine"]?.ToString() ?? "",
                    LotNumber = reader["lotNumber"]?.ToString() ?? "",
                    MaterialCode = reader["materialCode"]?.ToString() ?? "",
                    WeightLbs = reader.IsDBNull(reader.GetOrdinal("weightLbs"))
                                    ? (decimal?)null : reader.GetDecimal("weightLbs"),
                    BagNumber = reader["bagNumber"]?.ToString() ?? "",
                    SentDateTime = reader.GetDateTime("sentDateTime"),
                    Notes = reader["notes"]?.ToString() ?? "",

                    // NEW: map override fields safely
                    IsOverride = !reader.IsDBNull(reader.GetOrdinal("isOverride"))
                                    ? Convert.ToBoolean(reader["isOverride"])
                                    : false,
                    OverrideBy = reader["overrideBy"]?.ToString() ?? "",
                    OverrideAt = reader.IsDBNull(reader.GetOrdinal("overrideAt"))
                                    ? (DateTime?)null : reader.GetDateTime("overrideAt")
                };

                records.Add(m);
            }

            return records;
        }



        public async Task<string> GetMaterialCodeByLotAsync(string lotNumber)
        {
            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();

            const string query = "SELECT materialCode FROM powdermix WHERE lotNumber = @lot LIMIT 1";
            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@lot", lotNumber);

            var result = await command.ExecuteScalarAsync();
            return result?.ToString() ?? "";
        }
        public async Task<(string op, string mach)> GetLatestRunInfoAsync(string part, string prod, string run)
        {
            static string S(object? o) => o?.ToString() ?? "";

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // 1) Try the latest pressrun row for this part/prod/run
            const string qRun = @"
        SELECT operator, machine
        FROM pressrun
        WHERE part = @p AND prodNumber = @prod AND run = @run
        ORDER BY id DESC
        LIMIT 1;";

            await using (var cmd = new MySqlCommand(qRun, conn))
            {
                cmd.Parameters.AddWithValue("@p", part ?? "");
                cmd.Parameters.AddWithValue("@prod", prod ?? "");
                cmd.Parameters.AddWithValue("@run", run ?? "");

                await using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    var op = S(rd["operator"]);
                    var mach = S(rd["machine"]);
                    if (!string.IsNullOrWhiteSpace(op) || !string.IsNullOrWhiteSpace(mach))
                        return (op, mach);
                }
            }

            // 2) Fallback: use presssetup for the same part/prod/run
            // If your presssetup has an identity/timestamp you prefer, add ORDER BY <that> DESC
            const string qSetup = @"
        SELECT operator, machine
        FROM presssetup
        WHERE part = @p AND prodNumber = @prod AND run = @run
        LIMIT 1;";

            await using (var cmd2 = new MySqlCommand(qSetup, conn))
            {
                cmd2.Parameters.AddWithValue("@p", part ?? "");
                cmd2.Parameters.AddWithValue("@prod", prod ?? "");
                cmd2.Parameters.AddWithValue("@run", run ?? "");

                await using var rd2 = await cmd2.ExecuteReaderAsync();
                if (await rd2.ReadAsync())
                {
                    var op = S(rd2["operator"]);
                    var mach = S(rd2["machine"]);
                    return (op, mach);
                }
            }

            // Nothing found in either table
            return ("", "");
        }

        // PressMixBagChangeService.cs
        public async Task<string?> GetScheduledMaterialCodeAsync(string part, string prodNumber, string run)
        {
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            const string sql = @"
        SELECT materialCode
        FROM schedule
        WHERE part = @part
          AND prodNumber = @prod
          AND run = @run
        ORDER BY id DESC        
        LIMIT 1;";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@part", part);
            cmd.Parameters.AddWithValue("@prod", prodNumber);
            cmd.Parameters.AddWithValue("@run", run);

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString();
        }


        // Simple example. Replace with hashed PINs if you have them.
        public async Task<(bool ok, string name)> VerifySupervisorPinAsync(string pin)
        {
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            const string sql = @"SELECT name FROM supervisors WHERE pin = @pin LIMIT 1;";
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@pin", pin);
            var result = await cmd.ExecuteScalarAsync();
            if (result == null) return (false, "");
            return (true, result.ToString() ?? "");
        }


    }
}