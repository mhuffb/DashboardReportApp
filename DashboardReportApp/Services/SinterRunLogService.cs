using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using Microsoft.Extensions.Configuration;
using DashboardReportApp.Models;
using System.Data;
using Mysqlx.Crud;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

public class SinterRunLogService
{
    private readonly string _connectionStringMySQL;
    private readonly string _connectionStringDataflex;
    private string datatable = "sinterrun";

    public SinterRunLogService(IConfiguration configuration)
    {
        _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
        _connectionStringDataflex = configuration.GetConnectionString("DataflexConnection");
    }
    /// <summary>
    /// Get *all* runs in descending order by startDateTime.
    /// </summary>
    public async Task<List<SinterRunSkid>> GetAllRunsAsync()
    {
        var allRuns = new List<SinterRunSkid>();

        string query = @"
            SELECT id, timestamp, operator, prodNumber, run, part, component, oven, process, startDateTime, endDateTime, notes, open, skidNumber, pcs
            FROM " + datatable +
                " ORDER BY id DESC";

        await using var connection = new MySqlConnection(_connectionStringMySQL);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            DateTime timestamp = !reader.IsDBNull(reader.GetOrdinal("timestamp"))
                                 ? reader.GetDateTime("timestamp")
                                 : DateTime.MinValue;

            DateTime startDateTime = !reader.IsDBNull(reader.GetOrdinal("startDateTime"))
                                     ? reader.GetDateTime("startDateTime")
                                     : DateTime.MinValue;

            DateTime? endDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                                    ? (DateTime?)null
                                    : reader.GetDateTime("endDateTime");

            allRuns.Add(new SinterRunSkid
            {
                Id = reader.GetInt32("id"),
                Timestamp = timestamp,
                Operator = reader["operator"]?.ToString(),
                ProdNumber = reader["prodNumber"]?.ToString() ?? "N/A",
                Run = reader["run"]?.ToString() ?? "N/A",
                Part = reader["part"]?.ToString() ?? "N/A",
                Component = reader["component"]?.ToString(), // <-- New field
                Machine = reader["oven"]?.ToString(),
                Process = reader["process"]?.ToString(),
                StartDateTime = startDateTime,
                EndDateTime = endDateTime,
                Notes = reader["notes"]?.ToString(),
                Open = reader["open"] != DBNull.Value ? Convert.ToSByte(reader["open"]) : (sbyte)0,
                SkidNumber = reader["skidNumber"] != DBNull.Value ? reader.GetInt32("skidNumber") : 0,
                Pcs = !reader.IsDBNull(reader.GetOrdinal("pcs")) ? reader.GetInt32("pcs") : 0
            });
        }

        return allRuns;
    }


    // Get a list of operators from MySQL
    public List<string> GetOperators()
    {
        var operators = new List<string>();
        string query = "SELECT name FROM operators WHERE dept = 'sintering' ORDER BY name";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        operators.Add(reader["name"].ToString());
                    }
                }
            }
        }

        return operators;
    }

    // Fetch furnaces from MySQL
    public List<string> GetFurnaces()
    {
        var furnaces = new List<string>();
        string query = "SELECT equipment FROM equipment WHERE name = 'furnace' ORDER BY equipment";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        furnaces.Add(reader["equipment"].ToString());
                    }
                }
            }
        }

        return furnaces;
    }


    // Start a new skid (insert into sinterrun table)
    public void LoginToSkid(SinterRunSkid model)
    {
        Console.WriteLine("📌 StartSkid() Called:");
        Console.WriteLine($"➡️ Operator: {model.Operator}");
        Console.WriteLine($"➡️ Part: {model.Part}");
        Console.WriteLine($"➡️ ProdNumber: {model.ProdNumber}");
        Console.WriteLine($"➡️ Run: {model.Run}");
        Console.WriteLine($"➡️ Furnace: {model.Machine}");
        Console.WriteLine($"➡️ Process: {model.Process}");
        Console.WriteLine($"➡️ Notes: {model.Notes}");
        Console.WriteLine($"➡️ Pcs (requested): {model.Pcs}");

        // Build our insert statement
        // We'll keep pcs in the columns, because we DO want the first row to have it.
        string insertQuery = @"
        INSERT INTO sinterrun
            (prodNumber, run, part, startDateTime, operator, oven, process, notes, skidNumber, pcs)
        VALUES
            (@prodNumber, @run, @part, @startDateTime, @operator, @oven, @process, @notes, @skidNumber, @pcs)";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();

            // 1) Check if there's already a row with pcs > 0
            //    for the same (prodNumber, part, run, skidNumber).
            //    If so, that means "this skid" was already started before,
            //    and we only want the first row to hold pcs.
            //    We'll set pcs = 0 for subsequent inserts.
            string checkQuery = @"
            SELECT COUNT(*) 
            FROM sinterrun
            WHERE prodNumber = @prodNumber
              AND part       = @part
              AND (run = @run OR (@run IS NULL AND run IS NULL))
              AND skidNumber = @skidNumber
              AND pcs > 0";

            using (var checkCommand = new MySqlCommand(checkQuery, connection))
            {
                checkCommand.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                checkCommand.Parameters.AddWithValue("@part", model.Part);
                // run can be null, so handle that carefully
                checkCommand.Parameters.AddWithValue("@run", string.IsNullOrEmpty(model.Run) ? (object)DBNull.Value : model.Run);
                checkCommand.Parameters.AddWithValue("@skidNumber", model.SkidNumber);

                long existingRowsWithPcs = (long)checkCommand.ExecuteScalar();
                if (existingRowsWithPcs > 0)
                {
                    // Already a row with pcs for this skid => set pcs=0 for this new record
                    Console.WriteLine("⚠️  A row for this skid already has pcs. Setting current row's pcs = 0...");
                    model.Pcs = 0;
                }
            }

            // 2) Now do the INSERT
            using (var insertCommand = new MySqlCommand(insertQuery, connection))
            {
                insertCommand.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                insertCommand.Parameters.AddWithValue("@run", string.IsNullOrWhiteSpace(model.Run) ? (object)DBNull.Value : model.Run);
                insertCommand.Parameters.AddWithValue("@part", model.Part.ToUpper());
                insertCommand.Parameters.AddWithValue("@startDateTime", DateTime.Now);
                insertCommand.Parameters.AddWithValue("@operator", model.Operator);
                insertCommand.Parameters.AddWithValue("@oven", model.Machine);
                insertCommand.Parameters.AddWithValue("@process", model.Process);
                insertCommand.Parameters.AddWithValue("@notes", string.IsNullOrEmpty(model.Notes) ? (object)DBNull.Value : model.Notes);
                insertCommand.Parameters.AddWithValue("@skidNumber", model.SkidNumber);
                insertCommand.Parameters.AddWithValue("@pcs", model.Pcs);

                int rowsInserted = insertCommand.ExecuteNonQuery();
                Console.WriteLine($"✅ Rows inserted in sinterrun: {rowsInserted}");
            }

            // 3) If run isn't null or empty, close the pressrun skid
            if (!string.IsNullOrEmpty(model.Run))
            {
                string updatePressrunQuery = @"
                UPDATE pressrun
                SET open = 0
                WHERE prodNumber = @prodNumber
                  AND run        = @run
                  AND part       = @part
                  AND skidNumber = @skidNumber
                ORDER BY id DESC
                ";

                using (var updateCommand = new MySqlCommand(updatePressrunQuery, connection))
                {
                    updateCommand.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                    updateCommand.Parameters.AddWithValue("@part", model.Part);
                    updateCommand.Parameters.AddWithValue("@run", model.Run);
                    updateCommand.Parameters.AddWithValue("@skidNumber", model.SkidNumber);

                    int rowsAffected = updateCommand.ExecuteNonQuery();
                    Console.WriteLine($"✅ Rows Updated in pressrun: {rowsAffected}");
                }
            }
            else
            {
                // 4) Otherwise, if run is null, close the assembly skid
                string updateAssemblyQuery = @"
                UPDATE assembly
                SET open = 0
                WHERE prodNumber = @prodNumber
                  AND skidNumber = @skidNumber
                  AND part       = @part
                ORDER BY id DESC
                LIMIT 1";

                using (var updateCommand = new MySqlCommand(updateAssemblyQuery, connection))
                {
                    updateCommand.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                    updateCommand.Parameters.AddWithValue("@skidNumber", model.SkidNumber);
                    updateCommand.Parameters.AddWithValue("@part", model.Part);

                    int rowsAffected = updateCommand.ExecuteNonQuery();
                    Console.WriteLine($"✅ Rows Updated in assembly: {rowsAffected}");
                }
            }
        }
    }

    // Close a specific skid for a part and furnace, now including skidNumber
    // Service code: SinterRunLogService.cs

    public void LogoutOfSkid(string part, string run, string skidNumber, string prodNumber)
    {
        // 1) Close the sinter record by setting endDateTime
        string query = "UPDATE " + datatable + " " +
                       "SET endDateTime = NOW() " +
                       "WHERE part = @part " +
                       "  AND prodNumber = @prodNumber " +
                       "  AND skidNumber = @skidNumber";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@part", part);
                command.Parameters.AddWithValue("@prodNumber", prodNumber);
                command.Parameters.AddWithValue("@skidNumber", skidNumber);

                int rowsAffected = command.ExecuteNonQuery();
                Console.WriteLine($"✅ Rows Updated in {datatable}: {rowsAffected}");
            }
        }

        // 2) Re-open the green skid in the correct table
        //    If 'run' is not empty => belongs to pressrun
        //    If 'run' is empty     => belongs to assembly
        if (!string.IsNullOrWhiteSpace(run))
        {
            string updatePressrunQuery = @"
            UPDATE pressrun
               SET open = 1
             WHERE prodNumber = @prodNumber
               AND run        = @run
               AND part       = @part
               AND skidNumber = @skidNumber
             ORDER BY id DESC
             LIMIT 1";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                connection.Open();
                using (var updateCommand = new MySqlCommand(updatePressrunQuery, connection))
                {
                    updateCommand.Parameters.AddWithValue("@prodNumber", prodNumber);
                    updateCommand.Parameters.AddWithValue("@part", part);
                    updateCommand.Parameters.AddWithValue("@run", run);
                    updateCommand.Parameters.AddWithValue("@skidNumber", skidNumber);

                    int rowsAffected = updateCommand.ExecuteNonQuery();
                    Console.WriteLine($"✅ Rows Updated in pressrun: {rowsAffected}");
                }
            }
        }
        else
        {
            string updateAssemblyQuery = @"
            UPDATE assembly
               SET open = 1
             WHERE prodNumber = @prodNumber
               AND skidNumber = @skidNumber
               AND part       = @part
             ORDER BY id DESC
             LIMIT 1";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                connection.Open();
                using (var updateCommand = new MySqlCommand(updateAssemblyQuery, connection))
                {
                    updateCommand.Parameters.AddWithValue("@prodNumber", prodNumber);
                    updateCommand.Parameters.AddWithValue("@skidNumber", skidNumber);
                    updateCommand.Parameters.AddWithValue("@part", part);

                    int rowsAffected = updateCommand.ExecuteNonQuery();
                    Console.WriteLine($"✅ Rows Updated in assembly: {rowsAffected}");
                }
            }
        }
    }


    // End the current skid record by updating endDateTime, pcs, and notes,
    // and matching on prodNumber, run, part, and skidNumber.
    public void EndSkid(string prodNumber, string part, string skidNumber, string pcs,
                      string run, string oper, string furnace, string process, string notes)
    {

        string updateQuery = "UPDATE " + datatable + " " +
                             "SET endDateTime = NOW(), notes = @notes " +
                             "WHERE prodNumber = @prodNumber " +
                             "AND part = @part " +
                             "AND skidNumber = @skidNumber " +
                             "ORDER BY id DESC";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var updateCommand = new MySqlCommand(updateQuery, connection))
            {
                updateCommand.Parameters.AddWithValue("@prodNumber", prodNumber);
                updateCommand.Parameters.AddWithValue("@part", part);
                updateCommand.Parameters.AddWithValue("@skidNumber", skidNumber);
                updateCommand.Parameters.AddWithValue("@notes", notes);

                int rowsAffected = updateCommand.ExecuteNonQuery();
                Console.WriteLine($"✅ Rows Updated: {rowsAffected}");
            }


            // Check if 'run' is null or empty
            if (string.IsNullOrEmpty(run))
            {
                // If run is null (or empty), update 'pressrun'
                string updateQueryPressrun = @"
            UPDATE pressrun
            SET open = 0
            WHERE prodNumber = @prodNumber
              AND run        = @run
              AND part       = @part
              AND skidNumber = @skidNumber
            ORDER BY id DESC
            LIMIT 1"
                ;

                using (var updateCommand = new MySqlCommand(updateQueryPressrun, connection))
                {
                    updateCommand.Parameters.AddWithValue("@prodNumber", prodNumber);
                    updateCommand.Parameters.AddWithValue("@run", run ?? (object)DBNull.Value); // in case run == null
                    updateCommand.Parameters.AddWithValue("@part", part);
                    updateCommand.Parameters.AddWithValue("@skidNumber", skidNumber);

                    int rowsAffected = updateCommand.ExecuteNonQuery();
                    Console.WriteLine($"✅ Rows Updated in pressrun: {rowsAffected}");
                }
            }
            else
            {
                // If run is NOT null, update 'assembly'
                string updateQueryAssembly = @"
            UPDATE assembly
            SET open = 0
            WHERE prodNumber = @prodNumber
              AND skidNumber = @skidNumber"
                ;

                using (var updateCommand = new MySqlCommand(updateQueryAssembly, connection))
                {
                    updateCommand.Parameters.AddWithValue("@prodNumber", prodNumber);
                    updateCommand.Parameters.AddWithValue("@skidNumber", skidNumber);

                    int rowsAffected = updateCommand.ExecuteNonQuery();
                    Console.WriteLine($"✅ Rows Updated in assembly: {rowsAffected}");
                }
            }
        }

    }


    // End skids on the same furnace if one is already running
    public void EndSkidsByMachineIfNeeded(string furnace)
    {
        string query = "UPDATE " + datatable + " SET endDateTime = @endDateTime WHERE oven = @furnace AND endDateTime IS NULL";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@endDateTime", DateTime.Now);
                command.Parameters.AddWithValue("@furnace", furnace);
                command.ExecuteNonQuery();
            }
        }
    }
    public async Task<List<PressRunLogModel>> GetOpenGreenSkidsAsync()
    {
        var openGreenSkids = new List<PressRunLogModel>();

        string query = @"
(
    SELECT 
        MIN(id) AS id,
        MIN(timestamp) AS timestamp,
        prodNumber,
        GROUP_CONCAT(DISTINCT run) AS run,
        GROUP_CONCAT(DISTINCT part) AS part,
        GROUP_CONCAT(DISTINCT component) AS component,
        MAX(endDateTime) AS endDateTime,
        GROUP_CONCAT(DISTINCT operator) AS operator,
        GROUP_CONCAT(DISTINCT machine) AS machine,
        MIN(pcsStart) AS pcsStart,
        MAX(pcsEnd) AS pcsEnd,
        '' AS notes,
        skidNumber,
        MIN(startDateTime) AS startDateTime
    FROM pressrun
    WHERE open = 1 AND skidNumber > 0
    GROUP BY prodNumber, skidNumber
)

UNION ALL

(
    SELECT 
        MIN(id) AS id,
        MIN(timestamp) AS timestamp,
        prodNumber,
        '' AS run,
        GROUP_CONCAT(DISTINCT part) AS part,
        '' AS component,
        MAX(endDateTime) AS endDateTime,
        GROUP_CONCAT(DISTINCT operator) AS operator,
        '' AS machine,
        0 AS pcsStart,
        MAX(pcs) AS pcsEnd,
        '' AS notes,
        skidNumber,
        MAX(endDateTime) AS startDateTime
    FROM assembly
    WHERE open = 1
    GROUP BY prodNumber, skidNumber
)

ORDER BY part, skidNumber;



";

        await using var connection = new MySqlConnection(_connectionStringMySQL);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            // Retrieve the component value (from pressrun records; assembly rows will have empty string)
            string component = reader["component"]?.ToString() ?? "N/A";

            // If the component contains "C" (case-insensitive), skip this record.
            // (This condition will typically apply only to pressrun rows.)
            if (!string.IsNullOrEmpty(component) && component.IndexOf("C", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            // Retrieve the part value
            string part = reader["part"]?.ToString() ?? "N/A";

            openGreenSkids.Add(new PressRunLogModel
            {
                Id = reader.GetInt32("id"),
                Timestamp = reader.GetDateTime("timestamp"),
                ProdNumber = reader["prodNumber"]?.ToString() ?? "N/A",
                Run = reader["run"]?.ToString() ?? "N/A",
                Part = part,
                Component = component,
                EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                               ? (DateTime?)null
                               : reader.GetDateTime("endDateTime"),
                Operator = reader["operator"]?.ToString() ?? "N/A",
                SkidNumber = reader.GetInt32("skidNumber"),
                Machine = reader["machine"]?.ToString() ?? "N/A",
                PcsStart = reader.IsDBNull(reader.GetOrdinal("pcsStart"))
                               ? 0
                               : Convert.ToInt32(reader["pcsStart"]),
                PcsEnd = reader.IsDBNull(reader.GetOrdinal("pcsEnd"))
                               ? 0
                               : Convert.ToInt32(reader["pcsEnd"]),
                Notes = reader["notes"]?.ToString() ?? ""
            });
        }

        return openGreenSkids;
    }

}
