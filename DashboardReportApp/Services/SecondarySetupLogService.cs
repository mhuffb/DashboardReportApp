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
        private readonly SharedService _sharedService;


        public SecondarySetupLogService(IConfiguration configuration, SharedService sharedService)
        {
            _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
            _sharedService = sharedService;
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
                            Date = reader.IsDBNull(reader.GetOrdinal("date")) ? default(DateTime) : reader.GetDateTime("date"),

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
            string insertQuery = "INSERT INTO secondarysetup (date, operator, op, part, prodNumber, machine, run, pcs, scrapMach, scrapNonMach, notes, setupHours, open) " +
                                 "VALUES (@date, @operator, @op, @part, @prodNumber, @machine, @run, @pcs, @scrapMach, @scrapNonMach, @notes, @setupHours, 1)";

           // string updateQuery = "UPDATE schedule SET openToSecondary = 0 WHERE part = @part AND prodNumber = @prodNumber";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Insert the new setup record.
                        using (var command = new MySqlCommand(insertQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@date", DateTime.Now);
                            command.Parameters.AddWithValue("@operator", model.Operator);
                            command.Parameters.AddWithValue("@op", model.Op);
                            command.Parameters.AddWithValue("@part", model.Part);
                            command.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                            command.Parameters.AddWithValue("@machine", model.Machine);
                            command.Parameters.AddWithValue("@run", model.Run ?? 0);
                            command.Parameters.AddWithValue("@pcs", model.Pcs);
                            command.Parameters.AddWithValue("@scrapMach", model.ScrapMach);
                            command.Parameters.AddWithValue("@scrapNonMach", model.ScrapNonMach);
                            command.Parameters.AddWithValue("@notes", model.Notes);
                            command.Parameters.AddWithValue("@setupHours", model.SetupHours);

                            await command.ExecuteNonQueryAsync();
                        }

                        // Update the schedule record so that openToSecondary is set to 0.
                       // using (var command = new MySqlCommand(updateQuery, connection, transaction))
                       // {
                        //    command.Parameters.AddWithValue("@part", model.Part);
                       //     command.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                       //     await command.ExecuteNonQueryAsync();
                      //  }

                        transaction.Commit();
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
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


        public async Task<List<ScheduleItem>> GetAvailableScheduleItemsAsync()
        {
            const string sql = @"
WITH base AS (
  SELECT prodNumber, run, part, component, quantity, needsSintergySecondary, openToSecondary, secondaryWorkFlag
  FROM schedule
  WHERE needsSintergySecondary = 1 AND openToSecondary = 1
),
targets AS (
  SELECT prodNumber, run, part AS itemToSetup, quantity
  FROM base WHERE secondaryWorkFlag IN (1,3)
  UNION ALL
  SELECT prodNumber, run, component AS itemToSetup, quantity
  FROM base WHERE secondaryWorkFlag IN (2,3) AND component IS NOT NULL
),
dedup AS (
  SELECT
    prodNumber,
    itemToSetup,
    SUM(quantity) AS totalQty,
    CASE WHEN COUNT(DISTINCT run)=1 THEN MAX(run) ELSE NULL END AS singleRun,
    GROUP_CONCAT(DISTINCT run ORDER BY run SEPARATOR ',') AS runsCsv
  FROM targets
  GROUP BY prodNumber, itemToSetup
)
SELECT prodNumber, itemToSetup, totalQty, singleRun, runsCsv
FROM dedup
ORDER BY prodNumber DESC, itemToSetup ASC;";

            var list = new List<ScheduleItem>();
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();

            while (await rdr.ReadAsync())
            {
                var partId = rdr["itemToSetup"].ToString();

                list.Add(new ScheduleItem
                {
                    Part = partId,
                    ProdNumber = rdr["prodNumber"].ToString(),
                    Run = rdr.IsDBNull("singleRun") ? (int?)null : Convert.ToInt32(rdr["singleRun"]),
                    RunsCsv = rdr["runsCsv"]?.ToString(),
                    TotalQuantity = rdr.IsDBNull("totalQty") ? 0 : Convert.ToInt32(rdr["totalQty"]),

                    // CRITICAL: recompute ops for THIS target item (parent or component)
                    NumberOfSintergySecondaryOps = ComputeOpsFor(partId)
                });
            }
            return list;
        }



        private int ComputeOpsFor(string itemId)
        {
            var opsList = _sharedService.GetOrderOfOps(itemId) ?? new List<string>();
            int count = 0;
            foreach (var op in opsList)
            {
                var s = op ?? string.Empty;
                bool hasSintergy = s.IndexOf("Sintergy", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasMachining = s.IndexOf("Machin", StringComparison.OrdinalIgnoreCase) >= 0;
                if (hasSintergy && hasMachining) { count++; continue; }
                if (s.IndexOf("Tap", StringComparison.OrdinalIgnoreCase) >= 0) { count++; continue; }
                if (s.IndexOf("Honing", StringComparison.OrdinalIgnoreCase) >= 0) { count++; continue; }
            }
            return count;
        }




    }
}
