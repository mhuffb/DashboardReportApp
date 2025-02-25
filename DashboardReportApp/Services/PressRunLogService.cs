using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.Data.Common;
using System.Data;
using iText.Layout.Element;
using System.Reflection.PortableExecutable;

namespace DashboardReportApp.Services
{
    public class PressRunLogService
    {
        private readonly string _connectionStringMySQL;

        public PressRunLogService(IConfiguration config)
        {
            _connectionStringMySQL = config.GetConnectionString("MySQLConnection");
        }

        #region Device Mapping / Count

        /// <summary>
        /// Maps a machine value to its device IP.
        /// If the machine string already contains a dot, it is assumed to be an IP.
        /// Otherwise, it is mapped using a dictionary.
        /// </summary>
        private string MapMachineToIp(string machine)
        {
            if (machine.Contains("."))
            {
                return machine;
            }
            var dict = new Dictionary<string, string>
            {
                { "1", "192.168.1.30" },
                { "2", "192.168.1.31" },
                { "41", "192.168.1.32" },
                { "45", "192.168.1.33" },
                { "50", "192.168.1.34" },
                { "51", "192.168.1.35" },
                { "57", "192.168.1.36" },
                { "59", "192.168.1.37" },
                { "70", "192.168.1.38" },
                { "74", "192.168.1.39" },
                { "92", "192.168.1.40" },
                { "95", "192.168.1.41" },
                { "102", "192.168.1.42" },
                { "112", "192.168.1.43" },
                { "124", "192.168.1.44" },
                { "125", "192.168.1.45" },
                { "154", "192.168.1.46" },
                { "156", "192.168.1.47" },
                { "175", "192.168.1.48" }
                // Add additional mappings as needed.
            };
            if (dict.TryGetValue(machine, out var ip))
            {
                return ip;
            }
            throw new Exception($"No device found for machine: {machine}");
        }

        /// <summary>
        /// Attempts to query the device’s count.
        /// Returns an integer if successful; otherwise, null.
        /// This method uses multiple parsing strategies.
        /// </summary>
        public async Task<int?> TryGetDeviceCountOrNull(string machine)
        {
            string deviceIp;
            try
            {
                deviceIp = MapMachineToIp(machine);
            }
            catch
            {
                return null;
            }

            try
            {
                // Configure a 5-second timeout (adjust as needed)
                using var httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(5)
                };

                string url = $"http://{deviceIp}/api/picodata";
                HttpResponseMessage response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string json = (await response.Content.ReadAsStringAsync()).Trim();
                Console.WriteLine("Device JSON: " + json);

                // Attempt to parse "count_value" from a JSON object
                if (json.StartsWith("{"))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    using var doc = JsonDocument.Parse(json);
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("count_value", out JsonElement countElement))
                    {
                        if (countElement.ValueKind == JsonValueKind.Number && countElement.TryGetInt32(out int deviceCount))
                        {
                            return deviceCount;
                        }
                        else if (countElement.ValueKind == JsonValueKind.String)
                        {
                            string countStr = countElement.GetString();
                            if (int.TryParse(countStr, out int parsedCount))
                            {
                                return parsedCount;
                            }
                        }
                    }
                }

                // Fallback: check if JSON is just a plain integer
                if (int.TryParse(json, out int plainCount))
                {
                    return plainCount;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in TryGetDeviceCountOrNull: " + ex.Message);
            }
            return null;
        }



        #endregion

        #region Main Run Logic (Login, Logout, EndRun)

        public async Task HandleLoginWithCountAsync(PressRunLogModel formModel, int userCount)
        {
            const string insertMainRun = @"
INSERT INTO pressrun 
    (operator, part, machine, prodNumber, run, startDateTime, skidNumber)
VALUES 
    (@operator, @part, @machine, @prodNumber, @run, @startTime, 0)";
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(insertMainRun, conn);
            cmd.Parameters.AddWithValue("@operator", formModel.Operator);
            cmd.Parameters.AddWithValue("@part", formModel.Part);
            // Store the raw machine value.
            cmd.Parameters.AddWithValue("@machine", formModel.Machine);
            cmd.Parameters.AddWithValue("@prodNumber", formModel.ProdNumber);
            cmd.Parameters.AddWithValue("@run", formModel.Run);
            cmd.Parameters.AddWithValue("@startTime", formModel.StartDateTime);
            await cmd.ExecuteNonQueryAsync();
        }

        // Updated Logout: now accepts scrap and notes.
        public async Task HandleLogoutAsync(int runId, int finalCount, int scrap, string notes)
        {
            const string sql = @"
UPDATE pressrun
SET endDateTime = NOW(),
    scrap = @scrap,
    notes = @notes
WHERE id = @runId
  AND skidNumber = 0
LIMIT 1";
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@runId", runId);
            cmd.Parameters.AddWithValue("@finalCount", finalCount);
            cmd.Parameters.AddWithValue("@scrap", scrap);
            cmd.Parameters.AddWithValue("@notes", notes ?? "");
            await cmd.ExecuteNonQueryAsync();
        }

        // Updated EndRun: ends any open skid record, then ends the main run.
        public async Task HandleEndRunAsync(int runId, int finalCount, int scrap, string notes)
        {
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // 1) Retrieve the run identifier from the main run record.
            string mainRunIdentifier = "";
            const string selectMainRun = @"
SELECT run FROM pressrun
WHERE id = @runId AND skidNumber = 0
LIMIT 1";
            using (var selectCmd = new MySqlCommand(selectMainRun, conn))
            {
                selectCmd.Parameters.AddWithValue("@runId", runId);
                object obj = await selectCmd.ExecuteScalarAsync();
                if (obj != null)
                    mainRunIdentifier = obj.ToString();
            }

            if (!string.IsNullOrEmpty(mainRunIdentifier))
            {
                // 2) End any open skid record for this run and update its pcsEnd.
                const string endSkidQuery = @"
UPDATE pressrun
SET endDateTime = NOW(),
    pcsEnd = @finalCount,
    open = 1
WHERE run = @runIdentifier
  AND skidNumber > 0
  AND endDateTime IS NULL";
                using (var endSkidCmd = new MySqlCommand(endSkidQuery, conn))
                {
                    endSkidCmd.Parameters.AddWithValue("@runIdentifier", mainRunIdentifier);
                    endSkidCmd.Parameters.AddWithValue("@finalCount", finalCount);
                    await endSkidCmd.ExecuteNonQueryAsync();
                }
            }

            // 3) End the main run record.
            const string endMainRun = @"
UPDATE pressrun
SET endDateTime = NOW(),
    scrap = @scrap,
    notes = @notes
WHERE id = @runId
  AND skidNumber = 0
LIMIT 1";
            using (var endMainCmd = new MySqlCommand(endMainRun, conn))
            {
                endMainCmd.Parameters.AddWithValue("@runId", runId);
                endMainCmd.Parameters.AddWithValue("@scrap", scrap);
                endMainCmd.Parameters.AddWithValue("@notes", notes ?? "");
                await endMainCmd.ExecuteNonQueryAsync();
            }

            // 4) Mark the run as closed in the presssetup table.
            const string closeSetup = @"
UPDATE presssetup
SET open = 0
WHERE run = (
    SELECT run FROM pressrun WHERE id = @runId AND skidNumber = 0
)
LIMIT 1";
            using (var closeCmd = new MySqlCommand(closeSetup, conn))
            {
                closeCmd.Parameters.AddWithValue("@runId", runId);
                await closeCmd.ExecuteNonQueryAsync();
            }

           
        }

        #endregion

        #region Skid Logic

        public async Task HandleStartSkidAsync(PressRunLogModel model)
        {
            Console.WriteLine("Handling Start Skid in Service...");
            Console.WriteLine($"Run: {model.Run}");
            Console.WriteLine($"Machine: {model.Machine}");
            Console.WriteLine($"Part: {model.Part}");
            Console.WriteLine($"Operator: {model.Operator}");
            Console.WriteLine($"Prod Number: {model.ProdNumber}");
            Console.WriteLine($"Initial Pcs Start: {model.PcsStart}");

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // 1) Count existing skid records for this run (skidNumber > 0)
            int currentSkidNumber = 0;
            const string getSkids = @"
    SELECT IFNULL(MAX(skidNumber), 0)
    FROM pressrun
    WHERE run = @run
      AND skidNumber > 0";
            using (var cmd = new MySqlCommand(getSkids, conn))
            {
                cmd.Parameters.AddWithValue("@run", model.Run);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && int.TryParse(result.ToString(), out int c))
                    currentSkidNumber = c;
            }

            // 2) Only Fetch API count if `pcsStart` is NOT already provided
            if (model.PcsStart <= 0)
            {
                int? deviceCount = await TryGetDeviceCountOrNull(model.Machine);
                if (deviceCount.HasValue)
                {
                    model.PcsStart = deviceCount.Value;
                    Console.WriteLine($"Using API Device Count for Pcs Start: {model.PcsStart}");
                }
                else
                {
                    Console.WriteLine("Device count API failed, keeping original Pcs Start.");
                }
            }
            else
            {
                Console.WriteLine($"Using manually entered Pcs Start: {model.PcsStart}");
            }

            // 3) Insert the first skid if no skids exist
            if (currentSkidNumber == 0)
            {
                const string insertFirst = @"
        INSERT INTO pressrun (run, part, startDateTime, operator, machine, prodNumber, skidNumber, pcsStart)
        VALUES (@run, @part, NOW(), @operator, @machine, @prodNumber, 1, @pcsStart)";
                using var insCmd = new MySqlCommand(insertFirst, conn);
                insCmd.Parameters.AddWithValue("@run", model.Run);
                insCmd.Parameters.AddWithValue("@part", model.Part);
                insCmd.Parameters.AddWithValue("@operator", model.Operator);
                insCmd.Parameters.AddWithValue("@machine", model.Machine);
                insCmd.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                insCmd.Parameters.AddWithValue("@pcsStart", model.PcsStart);  // Now using manual input
                await insCmd.ExecuteNonQueryAsync();
            }
            else
            {
                // 4) End previous skid and insert a new one
                int newSkidNumber = currentSkidNumber + 1;

                const string endSkidSql = @"
        UPDATE pressrun p
        JOIN (
            SELECT run, MAX(skidNumber) AS maxSkid
            FROM pressrun
            WHERE run = @run
              AND skidNumber > 0
              AND endDateTime IS NULL
            GROUP BY run
        ) t ON p.run = @run AND p.skidNumber = t.maxSkid
        SET p.endDateTime = NOW(),
            p.pcsEnd = @pcsEnd,
            p.open = 1
        WHERE p.endDateTime IS NULL";
                using var endCmd = new MySqlCommand(endSkidSql, conn);
                endCmd.Parameters.AddWithValue("@run", model.Run);
                endCmd.Parameters.AddWithValue("@pcsEnd", model.PcsStart); // End previous skid with same count
                await endCmd.ExecuteNonQueryAsync();

                // 5) Insert a new skid
                const string insertNext = @"
        INSERT INTO pressrun (run, part, startDateTime, operator, machine, prodNumber, skidNumber, pcsStart)
        VALUES (@run, @part, NOW(), @operator, @machine, @prodNumber, @skidNumber, @pcsStart)";
                using var insSkid = new MySqlCommand(insertNext, conn);
                insSkid.Parameters.AddWithValue("@run", model.Run);
                insSkid.Parameters.AddWithValue("@part", model.Part);
                insSkid.Parameters.AddWithValue("@operator", model.Operator);
                insSkid.Parameters.AddWithValue("@machine", model.Machine);
                insSkid.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                insSkid.Parameters.AddWithValue("@skidNumber", newSkidNumber);
                insSkid.Parameters.AddWithValue("@pcsStart", model.PcsStart);  // Now using manual input
                await insSkid.ExecuteNonQueryAsync();
            }

            Console.WriteLine("Start Skid Processed Successfully.");
        }


        #endregion

        #region Retrieval

        public async Task<List<string>> GetOperatorsAsync()
        {
            var ops = new List<string>();
            const string sql = @"
SELECT name
FROM operators
WHERE dept = 'molding'
ORDER BY name";
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                ops.Add(rdr.GetString(0));
            }
            return ops;
        }

        public async Task<List<PressSetupModel>> GetOpenSetups()
        {
            var list = new List<PressSetupModel>();
            const string sql = @"
        SELECT id, timestamp, part, prodNumber, run, operator, endDateTime, machine, notes
        FROM presssetup
        WHERE open = 1
        ORDER BY part, run";

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var model = new PressSetupModel
                {
                    Id = rdr.GetInt32("id"),
                    Timestamp = rdr.GetDateTime("timestamp"),
                    Part = rdr.GetString("part"),
                    // Safely read prodNumber
                    ProdNumber = rdr.IsDBNull(rdr.GetOrdinal("prodNumber"))
                                 ? ""
                                 : rdr.GetString("prodNumber"),
                    Run = rdr.GetString("run"),
                    Operator = rdr.GetString("operator"),
                    EndDateTime = rdr.IsDBNull(rdr.GetOrdinal("endDateTime"))
                                 ? (DateTime?)null
                                 : rdr.GetDateTime("endDateTime"),
                    Machine = rdr.GetString("machine"),
                    Notes = rdr.IsDBNull(rdr.GetOrdinal("notes"))
                                 ? ""
                                 : rdr.GetString("notes")
                };

                list.Add(model);
            }
            return list;
        }



        public async Task<List<PressRunLogModel>> GetLoggedInRunsAsync()
        {
            var list = new List<PressRunLogModel>();
            const string sql = @"
SELECT id, timestamp, prodNumber, run, part, startDateTime, endDateTime,
       operator, machine, pcsStart, pcsEnd, scrap, notes, skidNumber
FROM pressrun
WHERE endDateTime IS NULL";

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(ParseRunFromReader(rdr));
            }
            return list;
        }

        public async Task<List<PressRunLogModel>> GetAllRunsAsync()
        {
            var list = new List<PressRunLogModel>();
            const string sql = @"
SELECT id, timestamp, prodNumber, run, part, startDateTime, endDateTime,
       operator, machine, pcsStart, pcsEnd, scrap, notes, skidNumber
FROM pressrun
ORDER BY id DESC";
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(ParseRunFromReader(rdr));
            }
            return list;
        }

        private PressRunLogModel ParseRunFromReader(DbDataReader rdr)
        {
            var model = new PressRunLogModel
            {
                Id = rdr.GetInt32("id"),
                Timestamp = rdr.GetDateTime("timestamp"),
                // Safely read prodNumber:
                ProdNumber = rdr.IsDBNull(rdr.GetOrdinal("prodNumber"))
                             ? ""
                             : rdr.GetString("prodNumber"),
                Run = rdr["run"]?.ToString(),
                Part = rdr["part"]?.ToString(),
                StartDateTime = rdr.GetDateTime("startDateTime"),
                EndDateTime = rdr.IsDBNull(rdr.GetOrdinal("endDateTime"))
                                ? null
                                : rdr.GetDateTime("endDateTime"),
                Operator = rdr["operator"]?.ToString(),
                Machine = rdr["machine"]?.ToString(),
                PcsStart = rdr.IsDBNull(rdr.GetOrdinal("pcsStart"))
                                ? 0
                                : rdr.GetInt32("pcsStart"),
                PcsEnd = rdr.IsDBNull(rdr.GetOrdinal("pcsEnd"))
                                ? 0
                                : rdr.GetInt32("pcsEnd"),
                Scrap = rdr.IsDBNull(rdr.GetOrdinal("scrap"))
                                ? 0
                                : rdr.GetInt32("scrap"),
                Notes = rdr["notes"]?.ToString(),
                SkidNumber = rdr.IsDBNull(rdr.GetOrdinal("skidNumber"))
                                ? 0
                                : rdr.GetInt32("skidNumber")
            };
            return model;
        }


        #endregion
    }
}
