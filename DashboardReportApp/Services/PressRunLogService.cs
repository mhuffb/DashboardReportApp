using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text.Json;
using System.Data;

namespace DashboardReportApp.Services
{
    public class PressRunLogService
    {
        private readonly string _connectionStringMySQL;

        public PressRunLogService(IConfiguration configuration)
        {
            _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
        }

        #region Data Retrieval Methods

        /// <summary>
        /// Returns all pressrun records where endDateTime is null.
        /// These are considered "open" runs (including both the main run record and any skid records).
        /// </summary>
        public async Task<List<PressRunLogModel>> GetLoggedInRunsAsync()
        {
            var loggedInRuns = new List<PressRunLogModel>();
            const string query = @"
                SELECT id, timestamp, run, part, startDateTime, endDateTime, 
                       operator, machine, pcsStart, pcsEnd, scrap, notes, skidcount
                FROM pressrun
                WHERE endDateTime IS NULL";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                loggedInRuns.Add(new PressRunLogModel
                {
                    Id = reader.GetInt32("id"),
                    Timestamp = reader.GetDateTime("timestamp"),
                    Run = reader["run"]?.ToString() ?? "N/A",
                    Part = reader["part"]?.ToString() ?? "N/A",
                    StartDateTime = reader.GetDateTime("startDateTime"),
                    EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                                   ? null
                                   : reader.GetDateTime("endDateTime"),
                    Operator = reader["operator"]?.ToString(),
                    Machine = reader["machine"]?.ToString(),
                    PcsStart = reader.IsDBNull(reader.GetOrdinal("pcsStart"))
                                  ? 0
                                  : reader.GetInt32("pcsStart"),
                    PcsEnd = reader.IsDBNull(reader.GetOrdinal("pcsEnd"))
                                  ? 0
                                  : reader.GetInt32("pcsEnd"),
                    Scrap = reader.IsDBNull(reader.GetOrdinal("scrap"))
                                  ? 0
                                  : reader.GetInt32("scrap"),
                    Notes = reader["notes"]?.ToString(),
                    SkidCount = reader.IsDBNull(reader.GetOrdinal("skidcount"))
                                  ? 0
                                  : reader.GetInt32("skidcount")
                });
            }

            return loggedInRuns;
        }

        /// <summary>
        /// Returns all pressrun records in descending order by ID (or startDateTime).
        /// </summary>
        public async Task<List<PressRunLogModel>> GetAllRunsAsync()
        {
            var allRuns = new List<PressRunLogModel>();
            const string query = @"
                SELECT id, timestamp, run, part, startDateTime, endDateTime, 
                       operator, machine, pcsStart, pcsEnd, scrap, notes, skidcount
                FROM pressrun
                ORDER BY id DESC";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                allRuns.Add(new PressRunLogModel
                {
                    Id = reader.GetInt32("id"),
                    Timestamp = reader.GetDateTime("timestamp"),
                    Run = reader["run"]?.ToString() ?? "N/A",
                    Part = reader["part"]?.ToString() ?? "N/A",
                    StartDateTime = reader.GetDateTime("startDateTime"),
                    EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                                   ? null
                                   : reader.GetDateTime("endDateTime"),
                    Operator = reader["operator"]?.ToString(),
                    Machine = reader["machine"]?.ToString(),
                    PcsStart = reader.IsDBNull(reader.GetOrdinal("pcsStart"))
                                  ? 0
                                  : reader.GetInt32("pcsStart"),
                    PcsEnd = reader.IsDBNull(reader.GetOrdinal("pcsEnd"))
                                  ? 0
                                  : reader.GetInt32("pcsEnd"),
                    Scrap = reader.IsDBNull(reader.GetOrdinal("scrap"))
                                  ? 0
                                  : reader.GetInt32("scrap"),
                    Notes = reader["notes"]?.ToString(),
                    SkidCount = reader.IsDBNull(reader.GetOrdinal("skidcount"))
                                  ? 0
                                  : reader.GetInt32("skidcount")
                });
            }

            return allRuns;
        }

        public async Task<List<string>> GetOperatorsAsync()
        {
            var operators = new List<string>();
            const string query = @"
                SELECT name 
                FROM operators 
                WHERE dept = 'molding'
                ORDER BY name";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                operators.Add(reader["name"]?.ToString() ?? "");
            }
            return operators;
        }

        public async Task<List<string>> GetEquipmentAsync()
        {
            var equipment = new List<string>();
            const string query = @"
                SELECT equipment 
                FROM equipment 
                WHERE name = 'press'
                  AND (department = 'molding' OR department = 'sizing')
                ORDER BY equipment";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                equipment.Add(reader["equipment"]?.ToString() ?? "");
            }
            return equipment;
        }

        public async Task<Dictionary<(string Part, string Run), string>> GetOpenPartsWithRunsAndMachinesAsync()
        {
            var partsWithDetails = new Dictionary<(string Part, string Run), string>();

            const string query = @"
                SELECT part, run, machine
                FROM presssetup
                WHERE open = 1
                ORDER BY part, run";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var part = reader["part"]?.ToString() ?? "";
                var runVal = reader["run"]?.ToString() ?? "";
                var machine = reader["machine"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(part) && !string.IsNullOrEmpty(runVal))
                {
                    partsWithDetails[(part, runVal)] = machine;
                }
            }
            return partsWithDetails;
        }

        public async Task<int> HandleLoginAsync(PressRunLogModel formModel)
        {
            // 1. Map machine -> IP
            string deviceIp = MapMachineToIp(formModel.Machine);

            // 2. Query device for pcsStart
            int deviceCountAtStart = await QueryDeviceForCountAsync(deviceIp);

            // 3. Insert the main run record (skidcount=0)
            const string query = @"
        INSERT INTO pressrun
            (operator, part, machine, run, startDateTime, open, skidcount, pcsStart)
        VALUES
            (@operator, @part, @machine, @run, @startDateTime, 1, 0, @pcsStart)";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);

            command.Parameters.AddWithValue("@operator", formModel.Operator);
            command.Parameters.AddWithValue("@part", formModel.Part);
            command.Parameters.AddWithValue("@machine", formModel.Machine);
            command.Parameters.AddWithValue("@run", formModel.Run);
            command.Parameters.AddWithValue("@startDateTime", formModel.StartDateTime);
            command.Parameters.AddWithValue("@pcsStart", deviceCountAtStart);

            await command.ExecuteNonQueryAsync();

            // Return the newly inserted auto-increment ID
            int newId = (int)command.LastInsertedId;
            return newId;
        }


        public async Task HandleLogoutAsync(int runId, int scrap, string notes)
        {
            // 1. Look up the existing run row so we know which machine to query.
            DateTime startTime = DateTime.MinValue;
            string machineValue = null;

            const string selectQuery = @"
        SELECT startDateTime, machine
        FROM pressrun
        WHERE id = @runId
          AND skidcount=0
        LIMIT 1";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var selectCmd = new MySqlCommand(selectQuery, connection);
            selectCmd.Parameters.AddWithValue("@runId", runId);

            await using var reader = await selectCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                startTime = reader.GetDateTime("startDateTime");
                machineValue = reader["machine"]?.ToString();
            }
            reader.Close();

            if (string.IsNullOrEmpty(machineValue))
            {
                throw new Exception($"No main run record found with id={runId} for logout.");
            }

            // 2. Map machine -> IP
            string deviceIp = MapMachineToIp(machineValue);

            // 3. Query device => pcsEnd
            int deviceCountAtEnd = await QueryDeviceForCountAsync(deviceIp);

            // 4. Update that row: set endDateTime, pcsEnd, scrap, notes
            DateTime endTime = DateTime.Now;

            const string updateQuery = @"
        UPDATE pressrun
        SET endDateTime = @endTime,
            pcsEnd      = @pcsEnd,
            scrap       = @scrap,
            notes       = @notes
        WHERE id = @runId AND skidcount=0
        LIMIT 1";

            await using var updateCmd = new MySqlCommand(updateQuery, connection);
            updateCmd.Parameters.AddWithValue("@endTime", endTime);
            updateCmd.Parameters.AddWithValue("@pcsEnd", deviceCountAtEnd);
            updateCmd.Parameters.AddWithValue("@scrap", scrap);
            updateCmd.Parameters.AddWithValue("@notes", notes ?? "");
            updateCmd.Parameters.AddWithValue("@runId", runId);

            await updateCmd.ExecuteNonQueryAsync();
        }


        public async Task HandleEndRunAsync(int runId, string part, int scrap, string notes)
        {
            DateTime endTime = DateTime.Now;

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();

            // 1. Find the main run row (skidcount=0) by runId.
            //    We need to retrieve its "machine" and "run" field to do further actions (like device query, or finding skids).
            DateTime startTimeOfMainRun = DateTime.MinValue;
            string runIdentifier = "";
            string machineValue = "";
            {
                const string selectMainRun = @"
            SELECT run, machine, startDateTime
            FROM pressrun
            WHERE id = @runId
              AND skidcount=0
            LIMIT 1";
                await using var selectCmd = new MySqlCommand(selectMainRun, connection);
                selectCmd.Parameters.AddWithValue("@runId", runId);

                await using var reader = await selectCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    runIdentifier = reader["run"]?.ToString() ?? "";
                    machineValue = reader["machine"]?.ToString() ?? "";
                    startTimeOfMainRun = reader.GetDateTime("startDateTime");
                }
                reader.Close();
            }

            if (string.IsNullOrEmpty(runIdentifier))
            {
                // No main run record found. Possibly handle error or just return.
                throw new Exception($"Main run record not found for runId={runId}.");
            }

            // 2. End the last skid for that run if it is still open (max skidcount, endDateTime=null).
            {
                // We can query the device for final count for the skid.
                // Map the machine -> IP
                if (!string.IsNullOrEmpty(machineValue))
                {
                    string deviceIp = MapMachineToIp(machineValue);
                    int deviceCountSkidEnd = await QueryDeviceForCountAsync(deviceIp);

                    // Use a JOIN to update the highest skid with null endDateTime
                    const string endSkidQuery = @"
                UPDATE pressrun p
                JOIN (
                    SELECT MAX(skidcount) AS maxSkid
                    FROM pressrun
                    WHERE run=@run
                      AND skidcount>0
                      AND endDateTime IS NULL
                ) t ON p.run=@run
                  AND p.skidcount = t.maxSkid
                SET p.endDateTime=@endTime,
                    p.pcsEnd=@pcsEnd
                WHERE p.endDateTime IS NULL";
                    await using var endSkidCmd = new MySqlCommand(endSkidQuery, connection);
                    endSkidCmd.Parameters.AddWithValue("@run", runIdentifier);
                    endSkidCmd.Parameters.AddWithValue("@endTime", endTime);
                    endSkidCmd.Parameters.AddWithValue("@pcsEnd", deviceCountSkidEnd);
                    await endSkidCmd.ExecuteNonQueryAsync();
                }
            }

            // 3. End the main run row: set endDateTime=now, scrap, notes, optionally pcsEnd if you want a final device reading
            //    from the same machine.
            int deviceCountRunEnd = 0;
            if (!string.IsNullOrEmpty(machineValue))
            {
                string deviceIp = MapMachineToIp(machineValue);
                deviceCountRunEnd = await QueryDeviceForCountAsync(deviceIp);
            }

            const string updateMainRun = @"
        UPDATE pressrun
        SET endDateTime = @endTime,
            scrap       = @scrap,
            notes       = @notes,
            pcsEnd      = @pcsEnd
        WHERE id = @runId
          AND skidcount=0
        LIMIT 1";
            await using (var updateCmd = new MySqlCommand(updateMainRun, connection))
            {
                updateCmd.Parameters.AddWithValue("@endTime", endTime);
                updateCmd.Parameters.AddWithValue("@scrap", scrap);
                updateCmd.Parameters.AddWithValue("@notes", notes ?? "");
                updateCmd.Parameters.AddWithValue("@pcsEnd", deviceCountRunEnd);
                updateCmd.Parameters.AddWithValue("@runId", runId);
                await updateCmd.ExecuteNonQueryAsync();
            }

            // 4. Mark the run as closed in presssetup (open=0).
            //    We'll find the part and run from the main run row. We already have runIdentifier and part from above.
            const string updateSetup = @"
        UPDATE presssetup
        SET open=0
        WHERE run=@run
          AND part=@part
        LIMIT 1";
            await using (var setupCmd = new MySqlCommand(updateSetup, connection))
            {
                setupCmd.Parameters.AddWithValue("@run", runIdentifier);
                setupCmd.Parameters.AddWithValue("@part", part);
                await setupCmd.ExecuteNonQueryAsync();
            }
        }


        #endregion

        #region Skid Logic

        /// <summary>
        /// Queries the device for the current press count (via the device IP).
        /// </summary>
        public async Task<int> QueryDeviceForCountAsync(string deviceIp)
        {
            string url = $"http://{deviceIp}/api/picodata";
            using (var httpClient = new HttpClient())
            {
                HttpResponseMessage response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonResponse);
                if (result != null
                    && result.TryGetValue("Count", out string countStr)
                    && int.TryParse(countStr, out int count))
                {
                    return count;
                }
                throw new Exception("Device response did not contain a valid count.");
            }
        }

        /// <summary>
        /// Query the presscount table for the count at or before a specified time (for run).
        /// If you prefer to rely on device queries, skip this.
        /// </summary>
        public async Task<int> QueryCountForTimeAsync(string run, DateTime targetTime)
        {
            int countValue = 0;
            const string query = @"
                SELECT `count`
                FROM presscount
                WHERE run = @run AND timestamp <= @targetTime
                ORDER BY timestamp DESC
                LIMIT 1";

            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@run", run);
            cmd.Parameters.AddWithValue("@targetTime", targetTime);
            var result = await cmd.ExecuteScalarAsync();
            if (result != null && int.TryParse(result.ToString(), out int value))
            {
                countValue = value;
            }
            return countValue;
        }

        /// <summary>
        /// Helper method to map machine => IP address so we never try connecting to "2" or "0.0.0.2".
        /// </summary>
        private string MapMachineToIp(string machine)
        {
            var mapping = new Dictionary<string, string>
            {
                { "2",           "192.168.1.254" },
                { "Machine102",  "192.168.1.17"  }
                // Add additional mappings here.
            };

            if (mapping.TryGetValue(machine, out string ip))
            {
                return ip;
            }
            else
            {
                throw new Exception($"No device found for machine: {machine}");
            }
        }

        /// <summary>
        /// Single method that either starts the first skid or ends the current skid and starts the next one.
        /// The main run record retains skidcount = 0, while each skid record has a non-null skidcount (1+).
        /// </summary>
        public async Task HandleStartSkidAsync(int runId, string run, string part, string operatorName, string machine, int skidCountFromForm)
        {
            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();

            // 1. Determine how many skid records exist for this run.
            int currentSkidCount = 0;
            const string getSkidCountQuery = @"
                SELECT IFNULL(MAX(skidcount), 0) 
                FROM pressrun 
                WHERE run = @run AND skidcount IS NOT NULL";
            await using (var cmd = new MySqlCommand(getSkidCountQuery, connection))
            {
                cmd.Parameters.AddWithValue("@run", run);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && int.TryParse(result.ToString(), out int count))
                {
                    currentSkidCount = count;
                }
            }

            // 2. Map machine to IP so we can query the device.
            string deviceIp = MapMachineToIp(machine);

            if (currentSkidCount == 0)
            {
                // No existing skid => start the first one with skidcount=1.
                int deviceCountAtStart = await QueryDeviceForCountAsync(deviceIp);

                const string insertSkidQuery = @"
                    INSERT INTO pressrun (run, part, startDateTime, operator, machine, open, skidcount, pcsStart)
                    VALUES (@run, @part, @startTime, @operator, @machine, 1, 1, @pcsStart)";
                await using (var insertCmd = new MySqlCommand(insertSkidQuery, connection))
                {
                    insertCmd.Parameters.AddWithValue("@run", run);
                    insertCmd.Parameters.AddWithValue("@part", part);
                    insertCmd.Parameters.AddWithValue("@startTime", DateTime.Now);
                    insertCmd.Parameters.AddWithValue("@operator", operatorName);
                    insertCmd.Parameters.AddWithValue("@machine", machine);
                    insertCmd.Parameters.AddWithValue("@pcsStart", deviceCountAtStart);
                    await insertCmd.ExecuteNonQueryAsync();
                }
            }
            else
            {
                // At least one skid exists => end the active skid, then insert a new one.
                DateTime endTime = DateTime.Now;

                // 2a. End the active skid record (max skidcount, endDateTime = null).
                const string endActiveSkidQuery = @"
                    UPDATE pressrun p
                    JOIN (
                        SELECT MAX(skidcount) AS maxSkid
                        FROM pressrun
                        WHERE run = @run AND skidcount IS NOT NULL AND endDateTime IS NULL
                    ) t ON p.run = @run AND p.skidcount = t.maxSkid
                    SET p.endDateTime = @endTime, p.pcsEnd = @pcsEnd";
                int deviceCountAtEnd = await QueryDeviceForCountAsync(deviceIp);

                await using (var endCmd = new MySqlCommand(endActiveSkidQuery, connection))
                {
                    endCmd.Parameters.AddWithValue("@endTime", endTime);
                    endCmd.Parameters.AddWithValue("@run", run);
                    endCmd.Parameters.AddWithValue("@pcsEnd", deviceCountAtEnd);
                    await endCmd.ExecuteNonQueryAsync();
                }

                // 2b. Insert new skid record with incremented skidcount.
                int newSkidCount = currentSkidCount + 1;
                int deviceCountNewStart = await QueryDeviceForCountAsync(deviceIp);

                const string insertNewSkidQuery = @"
                    INSERT INTO pressrun (run, part, startDateTime, operator, machine, open, skidcount, pcsStart)
                    VALUES (@run, @part, @startTime, @operator, @machine, 1, @newSkidCount, @pcsStart)";
                await using (var insertNewCmd = new MySqlCommand(insertNewSkidQuery, connection))
                {
                    insertNewCmd.Parameters.AddWithValue("@run", run);
                    insertNewCmd.Parameters.AddWithValue("@part", part);
                    insertNewCmd.Parameters.AddWithValue("@startTime", DateTime.Now);
                    insertNewCmd.Parameters.AddWithValue("@operator", operatorName);
                    insertNewCmd.Parameters.AddWithValue("@machine", machine);
                    insertNewCmd.Parameters.AddWithValue("@newSkidCount", newSkidCount);
                    insertNewCmd.Parameters.AddWithValue("@pcsStart", deviceCountNewStart);
                    await insertNewCmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Explicitly ends a skid by ID (optional).
        /// </summary>
        public async Task HandleEndSkidAsync(int skidRecordId)
        {
            // 1. Update the active skid record with the end datetime.
            DateTime endTime = DateTime.Now;
            const string updateSkidQuery = @"
                UPDATE pressrun
                SET endDateTime = @endTime
                WHERE id = @skidRecordId AND endDateTime IS NULL";
            await using var connection = new MySqlConnection(_connectionStringMySQL);
            await connection.OpenAsync();
            await using (var updateCmd = new MySqlCommand(updateSkidQuery, connection))
            {
                updateCmd.Parameters.AddWithValue("@endTime", endTime);
                updateCmd.Parameters.AddWithValue("@skidRecordId", skidRecordId);
                await updateCmd.ExecuteNonQueryAsync();
            }

            // 2. Retrieve startDateTime and run for this skid.
            DateTime startTime = DateTime.MinValue;
            string runIdentifier = "";
            const string selectQuery = @"
                SELECT startDateTime, run
                FROM pressrun
                WHERE id = @skidRecordId";
            await using (var selectCmd = new MySqlCommand(selectQuery, connection))
            {
                selectCmd.Parameters.AddWithValue("@skidRecordId", skidRecordId);
                await using var reader = await selectCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    startTime = reader.GetDateTime("startDateTime");
                    runIdentifier = reader["run"]?.ToString() ?? "";
                }
                reader.Close();
            }

            // 3. Query presscount table or device for counts. Example uses presscount table:
            int pcsStart = await QueryCountForTimeAsync(runIdentifier, startTime);
            int pcsEnd = await QueryCountForTimeAsync(runIdentifier, endTime);

            // 4. Update pcsStart and pcsEnd in pressrun.
            const string updateCounts = @"
                UPDATE pressrun
                SET pcsStart = @pcsStart, pcsEnd = @pcsEnd
                WHERE id = @skidRecordId";
            await using (var updateCountsCmd = new MySqlCommand(updateCounts, connection))
            {
                updateCountsCmd.Parameters.AddWithValue("@pcsStart", pcsStart);
                updateCountsCmd.Parameters.AddWithValue("@pcsEnd", pcsEnd);
                updateCountsCmd.Parameters.AddWithValue("@skidRecordId", skidRecordId);
                await updateCountsCmd.ExecuteNonQueryAsync();
            }
        }

        #endregion
    }
}
