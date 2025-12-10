using DashboardReportApp.Models;
using Microsoft.AspNetCore.Components;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Threading.Tasks;

namespace DashboardReportApp.Services
{
    public class PressSetupService
    {
        private readonly string _connectionStringMySQL;
        private readonly SharedService _sharedService;
        private readonly MoldingService _moldingService;

        public PressSetupService(IConfiguration config, SharedService sharedService)
        {
            _connectionStringMySQL = config.GetConnectionString("MySQLConnection");
            _sharedService = sharedService;
        }
        public List<PressSetupModel> GetAllRecords()
        {
            var records = new List<PressSetupModel>();

            const string sql = @"
        SELECT 
            id,
            part,
            component,
            prodNumber,
            run,
            operator,
            machine,
            startDateTime,
            endDateTime,
            pressType,
            difficulty,
            setupComp,
            assistanceReq,
            assistedBy,
            notes,
            materialCode,
            lotNumber
        FROM presssetup
        ORDER BY startDateTime DESC;";

            using var connection = new MySqlConnection(_connectionStringMySQL);
            connection.Open();

            using var command = new MySqlCommand(sql, connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(new PressSetupModel
                {
                    Id = reader.GetInt32("id"),
                    Part = reader["part"]?.ToString(),
                    Component = reader["component"]?.ToString(),
                    ProdNumber = reader["prodNumber"]?.ToString(),
                    Run = reader["run"]?.ToString(),
                    Operator = reader["operator"]?.ToString(),
                    Machine = reader["machine"]?.ToString(),
                    StartDateTime = reader["startDateTime"] as DateTime?,
                    EndDateTime = reader["endDateTime"] as DateTime?,
                    PressType = reader["pressType"]?.ToString(),
                    Difficulty = reader["difficulty"]?.ToString(),
                    SetupComp = reader["setupComp"]?.ToString(),
                    AssistanceReq = reader["assistanceReq"]?.ToString(),
                    AssistedBy = reader["assistedBy"]?.ToString(),
                    Notes = reader["notes"]?.ToString(),
                    MaterialCode = reader["materialCode"]?.ToString(),
                    LotNumber = reader["lotNumber"]?.ToString()
                });
            }

            return records;
        }


        public async Task LoginAsync(PressSetupLoginViewModel model)
        {
            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();

                // 🔹 Get current mix for this machine from pressmixbagchange
                var mix = await GetCurrentMixForMachineAsync(model.Machine);
                string mixLot = mix?.LotNumber;
                string mixCode = mix?.MaterialCode;

                string query = @"
            INSERT INTO presssetup 
                (part, component, run, operator, machine, startDateTime, open, prodNumber, materialCode, lotNumber) 
            VALUES 
                (@part, @component, @run, @operator, @machine, @startDateTime, @open, @prodNumber, @materialCode, @lotNumber);";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@part", model.Part?.ToUpper() ?? "");
                    command.Parameters.AddWithValue("@component", model.Component?.ToUpper() ?? "");
                    command.Parameters.AddWithValue("@run", model.Run);
                    command.Parameters.AddWithValue("@operator", model.Operator);
                    command.Parameters.AddWithValue("@machine", model.Machine);
                    command.Parameters.AddWithValue("@startDateTime", DateTime.Now);
                    command.Parameters.AddWithValue("@open", 0);
                    command.Parameters.AddWithValue("@prodNumber", model.ProdNumber ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@materialCode", (object?)mixCode ?? DBNull.Value);
                    command.Parameters.AddWithValue("@lotNumber", (object?)mixLot ?? DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }


        public async Task LogoutAsync(
      string partNumber,
      DateTime startDateTime,
      string difficulty,
      string assistanceRequired,
      string assistedBy,
      string setupComplete,
      string notes,
      string run)
        {
            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();

                // 1) Get the machine for this setup row
                string machine = null!;
                const string getMachineSql = @"
            SELECT machine
            FROM presssetup
            WHERE run = @run AND startDateTime = @startDateTime
            LIMIT 1;";

                using (var getCmd = new MySqlCommand(getMachineSql, connection))
                {
                    getCmd.Parameters.AddWithValue("@run", run);
                    getCmd.Parameters.AddWithValue("@startDateTime", startDateTime);
                    var result = await getCmd.ExecuteScalarAsync();
                    machine = result?.ToString();
                }

                // 2) Pull current material for that machine (from pressmixbagchange)
                string? materialCode = null;
                string? lotNumber = null;

                if (!string.IsNullOrWhiteSpace(machine))
                {
                    var mix = await GetCurrentMixForMachineAsync(machine);
                    materialCode = mix?.MaterialCode;
                    lotNumber = mix?.LotNumber;
                }

                // 3) Update presssetup row including material + lot
                string updateSql = @"
            UPDATE presssetup 
            SET endDateTime   = @endDateTime, 
                difficulty    = @difficulty, 
                assistanceReq = @assistanceReq, 
                assistedBy    = @assistedBy, 
                setupComp     = @setupComp, 
                open          = @open,
                notes         = @notes,
                materialCode  = @materialCode,
                lotNumber     = @lotNumber
            WHERE run = @run AND startDateTime = @startDateTime;";

                using (var command = new MySqlCommand(updateSql, connection))
                {
                    command.Parameters.AddWithValue("@endDateTime", DateTime.Now);
                    command.Parameters.AddWithValue("@difficulty", difficulty);
                    command.Parameters.AddWithValue("@assistanceReq", assistanceRequired);
                    command.Parameters.AddWithValue("@assistedBy",
                        assistanceRequired == "Assisted" ? assistedBy : (object?)DBNull.Value);
                    command.Parameters.AddWithValue("@setupComp", setupComplete);

                    if (setupComplete == "Yes")
                    {
                        command.Parameters.AddWithValue("@open", 1);
                        // close schedule row for this run
                        await CloseOnScheduleAsync(run);
                    }
                    else
                    {
                        command.Parameters.AddWithValue("@open", 0);
                    }

                    command.Parameters.AddWithValue("@notes", notes ?? "");
                    command.Parameters.AddWithValue("@run", run);
                    command.Parameters.AddWithValue("@startDateTime", startDateTime);

                    command.Parameters.AddWithValue("@materialCode", (object?)materialCode ?? DBNull.Value);
                    command.Parameters.AddWithValue("@lotNumber", (object?)lotNumber ?? DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        public List<string> GetOperators()
        {
            var operators = new List<string>();
            string query = "SELECT name FROM operators WHERE dept = 'molding' ORDER BY name";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            using (var command = new MySqlCommand(query, connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        operators.Add(reader["name"].ToString());
                    }
                }
            }

            return operators;
        }

        public List<string> GetEquipment()
        {
            var equipment = new List<string>();
            string query = "SELECT equipment FROM equipment WHERE department = 'molding' ORDER BY equipment";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            using (var command = new MySqlCommand(query, connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        equipment.Add(reader["equipment"].ToString());
                    }
                }
            }

            return equipment;
        }

        public List<string> GetTrainers()
        {
            var trainers = new List<string>();
            string query = "SELECT name FROM operators WHERE dept = 'molding' AND level = 'trainer' ORDER BY name";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            using (var command = new MySqlCommand(query, connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        trainers.Add(reader["name"].ToString());
                    }
                }
            }

            return trainers;
        }
      

        public List<Scheduled> GetScheduledParts()
        {
            var records = new List<Scheduled>();
            string query = "SELECT * FROM schedule where open = 1 order by id desc";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            using (var command = new MySqlCommand(query, connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // Retrieve values
                        var component = reader["component"].ToString();

                        
                        // If  is null/empty and component contains "Y", skip.
                        if (!string.IsNullOrWhiteSpace(component) &&
                            component.IndexOf("Y", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                           continue;
                        }
                        

                        // If component contains "PC", skip this record.
                        if (!string.IsNullOrWhiteSpace(component) &&
                            component.IndexOf("PC", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        // If none of the conditions met, add the record.
                        records.Add(new Scheduled
                        {
                            Id = Convert.ToInt32(reader["id"]),
                            Part = reader["part"].ToString(),
                            Component = component,
                            ProdNumber = reader["ProdNumber"].ToString(),
                            Run = reader["run"].ToString(),
                        });
                    }
                }
            }
            return records;
        }

        public async Task CloseOnScheduleAsync(string currentRun)
        {
            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                string query = @"UPDATE Schedule 
                         SET open = 0 
                         WHERE run = @run";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@run", currentRun);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
        public async Task ResetPressCounterAsync(string machine)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(machine))
                {
                    Console.WriteLine("[ResetPressCounterAsync] ERROR: 'machine' parameter is null or empty.");
                    throw new ArgumentNullException(nameof(machine), "Machine parameter cannot be null or empty.");
                }

                // If the machine value contains colons, extract the last part.
                if (machine.Contains(":"))
                {
                    var parts = machine.Split(':');
                    string original = machine;
                    machine = parts.Last();
                    Console.WriteLine($"[ResetPressCounterAsync] Parsed machine id from '{original}' to '{machine}'.");
                }

                // Use the centralized mapping to obtain the device IP.
                string deviceIP = _sharedService.GetDeviceIp(machine);
                Console.WriteLine($"[ResetPressCounterAsync] Machine '{machine}' mapped to device IP: {deviceIP}");

                using (var client = new HttpClient())
                {
                    // Only send count_value to reset the counter (do not update press_value).
                    var content = new FormUrlEncodedContent(new[]
                    {
                new KeyValuePair<string, string>("count_value", "0")
            });

                    string requestUrl = $"http://{deviceIP}/update";
                    Console.WriteLine($"[ResetPressCounterAsync] Sending POST request to: {requestUrl}");
                    Console.WriteLine("[ResetPressCounterAsync] Request content: count_value=0");

                    var response = await client.PostAsync(requestUrl, content);
                    Console.WriteLine($"[ResetPressCounterAsync] Received HTTP status code: {response.StatusCode}");
                    response.EnsureSuccessStatusCode();
                    Console.WriteLine("[ResetPressCounterAsync] Counter reset successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ResetPressCounterAsync] Exception occurred: {ex}");
                throw;
            }
        }


        // PressSetupService.cs
        public (List<PressSetupModel> Rows, int Total) GetRecordsPage(
            int page, int pageSize, string? search, string? sortBy, string sortDir,
            DateTime? startDate, DateTime? endDate, string? machine)
        {
            var sortColumn = (sortBy ?? "StartDateTime") switch
            {
                "Id" => "id",
                "Part" => "part",
                "Component" => "component",
                "ProdNumber" => "prodNumber",
                "Run" => "run",
                "Operator" => "operator",
                "Machine" => "machine",
                "StartDateTime" => "startDateTime",
                "EndDateTime" => "endDateTime",
                "MaterialCode" => "materialCode",   
                "LotNumber" => "lotNumber",     
                _ => "startDateTime"
            };
            var direction = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
            var offset = (page - 1) * pageSize;

            var rows = new List<PressSetupModel>();
            int total = 0;

            using var conn = new MySqlConnection(_connectionStringMySQL);
            conn.Open();

            // ----- Build WHERE dynamically -----
            var whereParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
                whereParts.Add("(part LIKE @q OR component LIKE @q OR prodNumber LIKE @q OR run LIKE @q OR operator LIKE @q OR machine LIKE @q)");

            if (!string.IsNullOrWhiteSpace(machine))
                whereParts.Add("machine = @machine"); // exact match from dropdown

            if (startDate.HasValue && endDate.HasValue)
                whereParts.Add("startDateTime >= @start AND startDateTime < @endExclusive");
            else if (startDate.HasValue)
                whereParts.Add("startDateTime >= @start");
            else if (endDate.HasValue)
                whereParts.Add("startDateTime < @endExclusive");

            var whereSql = whereParts.Count > 0 ? $"WHERE {string.Join(" AND ", whereParts)}" : "";

            // Inclusive end-of-day handling: [start, end 23:59:59.999...)
            var start = startDate?.Date;
            var endExclusive = endDate?.Date.AddDays(1);

            // ----- Count -----
            using (var countCmd = new MySqlCommand($@"SELECT COUNT(*) FROM presssetup {whereSql}", conn))
            {
                if (!string.IsNullOrWhiteSpace(search))
                    countCmd.Parameters.AddWithValue("@q", $"%{search}%");
                if (!string.IsNullOrWhiteSpace(machine))
                    countCmd.Parameters.AddWithValue("@machine", machine);
                if (start.HasValue)
                    countCmd.Parameters.AddWithValue("@start", start.Value);
                if (endExclusive.HasValue)
                    countCmd.Parameters.AddWithValue("@endExclusive", endExclusive.Value);

                total = Convert.ToInt32(countCmd.ExecuteScalar());
            }

            // ----- Page -----
            using (var cmd = new MySqlCommand($@"
    SELECT id, part, component, prodNumber, run, operator, machine,
           startDateTime, endDateTime, pressType, difficulty, setupComp,
           assistanceReq, assistedBy, notes,
           materialCode, lotNumber           -- 👈 NEW
    FROM presssetup
    {whereSql}
    ORDER BY {sortColumn} {direction}
    LIMIT @take OFFSET @skip;", conn))

            {
                if (!string.IsNullOrWhiteSpace(search))
                    cmd.Parameters.AddWithValue("@q", $"%{search}%");
                if (!string.IsNullOrWhiteSpace(machine))
                    cmd.Parameters.AddWithValue("@machine", machine);
                if (start.HasValue)
                    cmd.Parameters.AddWithValue("@start", start.Value);
                if (endExclusive.HasValue)
                    cmd.Parameters.AddWithValue("@endExclusive", endExclusive.Value);

                cmd.Parameters.AddWithValue("@take", pageSize);
                cmd.Parameters.AddWithValue("@skip", offset);

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    rows.Add(new PressSetupModel
                    {
                        Id = rdr.GetInt32("id"),
                        Part = rdr["part"]?.ToString(),
                        Component = rdr["component"]?.ToString(),
                        ProdNumber = rdr["prodNumber"]?.ToString(),
                        Run = rdr["run"]?.ToString(),
                        Operator = rdr["operator"]?.ToString(),
                        Machine = rdr["machine"]?.ToString(),
                        StartDateTime = rdr["startDateTime"] as DateTime?,
                        EndDateTime = rdr["endDateTime"] as DateTime?,
                        PressType = rdr["pressType"]?.ToString(),
                        Difficulty = rdr["difficulty"]?.ToString(),
                        SetupComp = rdr["setupComp"]?.ToString(),
                        AssistanceReq = rdr["assistanceReq"]?.ToString(),
                        AssistedBy = rdr["assistedBy"]?.ToString(),
                        Notes = rdr["notes"]?.ToString(),
                        MaterialCode = rdr["materialCode"]?.ToString(),   
                        LotNumber = rdr["lotNumber"]?.ToString()
                    });
                }
            }

            return (rows, total);
        }

        public async Task RefreshMaterialFromPressAsync(long setupId)
        {
            using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // 1) Find the row + machine
            string getSql = @"SELECT machine FROM presssetup WHERE id = @id LIMIT 1";
            string machine = null;

            using (var cmd = new MySqlCommand(getSql, conn))
            {
                cmd.Parameters.AddWithValue("@id", setupId);
                var result = await cmd.ExecuteScalarAsync();
                machine = result?.ToString();
            }

            if (string.IsNullOrWhiteSpace(machine))
            {
                Console.WriteLine($"[RefreshMaterialFromPressAsync] No machine found for id={setupId}");
                return;
            }

            // 2) Get current material from press
            // 2) Get current material from press
            // 2) Get current material from pressmixbagchange (same logic as PressRunLogService)
            var current = await GetCurrentMixForMachineAsync(machine);
            var materialCode = current?.MaterialCode;
            var lotNumber = current?.LotNumber;



            // 3) Update the presssetup row
            string updateSql = @"
        UPDATE presssetup
        SET materialCode = @materialCode,
            lotNumber    = @lotNumber
        WHERE id = @id";

            using (var cmd = new MySqlCommand(updateSql, conn))
            {
                cmd.Parameters.AddWithValue("@id", setupId);
                cmd.Parameters.AddWithValue("@materialCode", (object?)materialCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@lotNumber", (object?)lotNumber ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<(string LotNumber, string MaterialCode)?> GetMaterialForSetupAsync(
    string prodNumber,
    string run)
        {
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            const string sql = @"
        SELECT lotNumber, materialCode
        FROM presssetup
        WHERE prodNumber = @prodNumber
          AND run        = @run
        ORDER BY startDateTime DESC
        LIMIT 1;";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@prodNumber", prodNumber ?? "");
            cmd.Parameters.AddWithValue("@run", run ?? "");

            await using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (!await rdr.ReadAsync())
                return null;

            var lot = rdr["lotNumber"]?.ToString() ?? string.Empty;
            var mat = rdr["materialCode"]?.ToString() ?? string.Empty;
            return (lot, mat);
        }
        public async Task<(string LotNumber, string MaterialCode)?> GetCurrentMixForMachineAsync(string machine)
        {
            if (string.IsNullOrWhiteSpace(machine))
                return null;

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            const string sql = @"
        SELECT LotNumber, MaterialCode
        FROM pressmixbagchange
        WHERE Machine = @machine
        ORDER BY id DESC
        LIMIT 1;";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@machine", machine);

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (!await reader.ReadAsync())
                return null;

            var lot = reader["LotNumber"]?.ToString() ?? string.Empty;
            var mat = reader["MaterialCode"]?.ToString() ?? string.Empty;

            return (lot, mat);
        }


    }
}
