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
                { "2", "192.168.1.254" },
                { "Machine102", "192.168.1.17" }
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
                using var httpClient = new HttpClient();
                string url = $"http://{deviceIp}/api/picodata";
                HttpResponseMessage response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string json = (await response.Content.ReadAsStringAsync()).Trim();
                Console.WriteLine("Device JSON: " + json);

                // 1. Try to parse as a plain integer.
                if (int.TryParse(json, out int plainCount))
                {
                    return plainCount;
                }

                // 2. If the JSON is quoted, trim the quotes.
                if (json.StartsWith("\"") && json.EndsWith("\""))
                {
                    string trimmed = json.Trim('"');
                    if (int.TryParse(trimmed, out int trimmedCount))
                    {
                        return trimmedCount;
                    }
                }

                // 3. Parse the JSON using JsonDocument.
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                using var doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out int numCount))
                {
                    return numCount;
                }
                else if (root.ValueKind == JsonValueKind.String)
                {
                    string s = root.GetString();
                    if (int.TryParse(s, out int strCount))
                    {
                        return strCount;
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("Count", out JsonElement countElement) ||
                        root.TryGetProperty("count", out countElement))
                    {
                        if (countElement.ValueKind == JsonValueKind.Number && countElement.TryGetInt32(out int deviceCount))
                        {
                            return deviceCount;
                        }
                        else if (countElement.ValueKind == JsonValueKind.String &&
                                 int.TryParse(countElement.GetString(), out int deviceCountFromStr))
                        {
                            return deviceCountFromStr;
                        }
                    }
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
    (operator, part, machine, run, startDateTime, open, skidcount, pcsStart)
VALUES 
    (@operator, @part, @machine, @run, @startTime, 1, 0, @pcsStart)";
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(insertMainRun, conn);
            cmd.Parameters.AddWithValue("@operator", formModel.Operator);
            cmd.Parameters.AddWithValue("@part", formModel.Part);
            // Store the raw machine value.
            cmd.Parameters.AddWithValue("@machine", formModel.Machine);
            cmd.Parameters.AddWithValue("@run", formModel.Run);
            cmd.Parameters.AddWithValue("@startTime", formModel.StartDateTime);
            cmd.Parameters.AddWithValue("@pcsStart", userCount);
            await cmd.ExecuteNonQueryAsync();
        }

        // Updated Logout: now accepts scrap and notes.
        public async Task HandleLogoutAsync(int runId, int finalCount, int scrap, string notes)
        {
            const string sql = @"
UPDATE pressrun
SET endDateTime = NOW(),
    pcsEnd = @finalCount,
    scrap = @scrap,
    notes = @notes
WHERE id = @runId
  AND skidcount = 0
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
WHERE id = @runId AND skidcount = 0
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
    pcsEnd = @finalCount
WHERE run = @runIdentifier
  AND skidcount > 0
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
    pcsEnd = @finalCount,
    scrap = @scrap,
    notes = @notes
WHERE id = @runId
  AND skidcount = 0
LIMIT 1";
            using (var endMainCmd = new MySqlCommand(endMainRun, conn))
            {
                endMainCmd.Parameters.AddWithValue("@runId", runId);
                endMainCmd.Parameters.AddWithValue("@finalCount", finalCount);
                endMainCmd.Parameters.AddWithValue("@scrap", scrap);
                endMainCmd.Parameters.AddWithValue("@notes", notes ?? "");
                await endMainCmd.ExecuteNonQueryAsync();
            }

            // 4) Mark the run as closed in the presssetup table.
            const string closeSetup = @"
UPDATE presssetup
SET open = 0
WHERE run = (
    SELECT run FROM pressrun WHERE id = @runId AND skidcount = 0
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

        public async Task HandleStartSkidAsync(int runId, string run, string part,
                                               string operatorName, string machine, int skidCountFromForm)
        {
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // 1) Count existing skid records for this run (skidcount > 0)
            int currentSkidCount = 0;
            const string getSkids = @"
SELECT IFNULL(MAX(skidcount), 0)
FROM pressrun
WHERE run = @run
  AND skidcount > 0";
            using (var cmd = new MySqlCommand(getSkids, conn))
            {
                cmd.Parameters.AddWithValue("@run", run);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && int.TryParse(result.ToString(), out int c))
                    currentSkidCount = c;
            }

            // 2) Get device count using the raw machine value.
            int? deviceCount = await TryGetDeviceCountOrNull(machine);

            if (currentSkidCount == 0)
            {
                const string insertFirst = @"
INSERT INTO pressrun (run, part, startDateTime, operator, machine, open, skidcount, pcsStart)
VALUES (@run, @part, NOW(), @operator, @machine, 1, 1, @pcsStart)";
                using var insCmd = new MySqlCommand(insertFirst, conn);
                insCmd.Parameters.AddWithValue("@run", run);
                insCmd.Parameters.AddWithValue("@part", part);
                insCmd.Parameters.AddWithValue("@operator", operatorName);
                insCmd.Parameters.AddWithValue("@machine", machine);
                if (deviceCount.HasValue)
                    insCmd.Parameters.AddWithValue("@pcsStart", deviceCount.Value);
                else
                    insCmd.Parameters.AddWithValue("@pcsStart", DBNull.Value);
                await insCmd.ExecuteNonQueryAsync();
            }
            else
            {
                DateTime now = DateTime.Now;
                const string endSkidSql = @"
UPDATE pressrun p
JOIN (
    SELECT run, MAX(skidcount) AS maxSkid
    FROM pressrun
    WHERE run = @run
      AND skidcount > 0
      AND endDateTime IS NULL
    GROUP BY run
) t ON p.run = @run AND p.skidcount = t.maxSkid
SET p.endDateTime = NOW(),
    p.pcsEnd = @pcsEnd
WHERE p.endDateTime IS NULL";
                using var endCmd = new MySqlCommand(endSkidSql, conn);
                endCmd.Parameters.AddWithValue("@run", run);
                if (deviceCount.HasValue)
                    endCmd.Parameters.AddWithValue("@pcsEnd", deviceCount.Value);
                else
                    endCmd.Parameters.AddWithValue("@pcsEnd", DBNull.Value);
                await endCmd.ExecuteNonQueryAsync();

                int newSkidCount = currentSkidCount + 1;
                int? newSkidStart = await TryGetDeviceCountOrNull(machine);
                const string insertNext = @"
INSERT INTO pressrun (run, part, startDateTime, operator, machine, open, skidcount, pcsStart)
VALUES (@run, @part, NOW(), @operator, @machine, 1, @skidcount, @pcsStart)";
                using var insSkid = new MySqlCommand(insertNext, conn);
                insSkid.Parameters.AddWithValue("@run", run);
                insSkid.Parameters.AddWithValue("@part", part);
                insSkid.Parameters.AddWithValue("@operator", operatorName);
                insSkid.Parameters.AddWithValue("@machine", machine);
                insSkid.Parameters.AddWithValue("@skidcount", newSkidCount);
                if (newSkidStart.HasValue)
                    insSkid.Parameters.AddWithValue("@pcsStart", newSkidStart.Value);
                else
                    insSkid.Parameters.AddWithValue("@pcsStart", DBNull.Value);
                await insSkid.ExecuteNonQueryAsync();
            }
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

        public async Task<Dictionary<(string Part, string Run), string>> GetOpenPartsWithRunsAndMachinesAsync()
        {
            var dict = new Dictionary<(string, string), string>();
            const string sql = @"
SELECT part, run, machine
FROM presssetup
WHERE open = 1
ORDER BY part, run";
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var part = rdr.GetString("part");
                var run = rdr.GetString("run");
                var mach = rdr.GetString("machine");
                dict[(part, run)] = mach;
            }
            return dict;
        }

        public async Task<List<PressRunLogModel>> GetLoggedInRunsAsync()
        {
            var list = new List<PressRunLogModel>();
            const string sql = @"
SELECT id, timestamp, run, part, startDateTime, endDateTime,
       operator, machine, pcsStart, pcsEnd, scrap, notes, skidcount
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
SELECT id, timestamp, run, part, startDateTime, endDateTime,
       operator, machine, pcsStart, pcsEnd, scrap, notes, skidcount
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
                SkidCount = rdr.IsDBNull(rdr.GetOrdinal("skidcount"))
                                ? 0
                                : rdr.GetInt32("skidcount")
            };
            return model;
        }

        #endregion
    }
}
