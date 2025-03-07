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
        Console.WriteLine($"➡️ Run: {model.Run}");  // Check if this prints correctly
        Console.WriteLine($"➡️ Furnace: {model.Machine}");
        Console.WriteLine($"➡️ Process: {model.Process}");
        Console.WriteLine($"➡️ Notes: {model.Notes}");
        Console.WriteLine($"Pcs:  {model.Pcs}");

        string query = "INSERT INTO " + datatable + " (prodNumber,  run, part, startDateTime, operator, oven, process, notes, skidNumber, pcs ) " +
                   "VALUES (@prodNumber, @run, @part,  @startDateTime, @operator, @oven, @process, @notes, @skidNumber, @pcs)";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {

                command.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                command.Parameters.AddWithValue("@run", string.IsNullOrWhiteSpace(model.Run) ? (object)DBNull.Value : model.Run);

                command.Parameters.AddWithValue("@part", model.Part.ToUpper());
                command.Parameters.AddWithValue("@startDateTime", DateTime.Now);
                command.Parameters.AddWithValue("@operator", model.Operator);
                command.Parameters.AddWithValue("@oven", model.Machine);
                command.Parameters.AddWithValue("@process", model.Process);
                command.Parameters.AddWithValue("@notes", string.IsNullOrEmpty(model.Notes) ? DBNull.Value : model.Notes);
                command.Parameters.AddWithValue("@skidNumber", model.SkidNumber);
                command.Parameters.AddWithValue("@pcs", model.Pcs);

                command.ExecuteNonQuery();
            }

            // If run isn't null or empty
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
            LIMIT 1";

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
                // If run is null or empty, update the assembly table
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
    public void LogoutOfSkid(string part, string run, string skidNumber, string prodNumber)
    {
        string query = "UPDATE " + datatable + " SET endDateTime = NOW() " +
                       "WHERE part = @part AND prodNumber = @prodNumber AND skidNumber = @skidNumber";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@part", part);
                command.Parameters.AddWithValue("@prodNumber", prodNumber);
                command.Parameters.AddWithValue("@skidNumber", skidNumber);

                int rowsAffected = command.ExecuteNonQuery();
                Console.WriteLine($"✅ Rows Updated: {rowsAffected}");
            }
        }

        if (string.IsNullOrWhiteSpace(run))
        {
            // If run is null/empty, update the pressrun table.
            string updateQuery2 = "UPDATE pressrun " +
                                  "SET open = 1, run = @run, skidNumber = @skidNumber " +
                                  "ORDER BY id DESC LIMIT 1 " +
                                  "WHERE run = @run AND skidNumber = @skidNumber";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                connection.Open();
                using (var updateCommand = new MySqlCommand(updateQuery2, connection))
                {
                    updateCommand.Parameters.AddWithValue("@run", run);
                    updateCommand.Parameters.AddWithValue("@skidNumber", skidNumber);

                    int rowsAffected = updateCommand.ExecuteNonQuery();
                    Console.WriteLine($"✅ Rows Updated in pressrun: {rowsAffected}");
                }
            }
        }
        else
        {
            // Otherwise, update the assembly table using prodNumber and skidNumber.
            string updateAssemblyQuery = "UPDATE assembly " +
                                         "SET open = 1 " +
                                         "WHERE prodNumber = @prodNumber " +
                                         "AND skidNumber = @skidNumber";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                connection.Open();
                using (var updateCommand = new MySqlCommand(updateAssemblyQuery, connection))
                {
                    updateCommand.Parameters.AddWithValue("@prodNumber", prodNumber);
                    updateCommand.Parameters.AddWithValue("@skidNumber", skidNumber);

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
                             "SET endDateTime = NOW(), pcs = @pcs, notes = @notes " +
                             "WHERE prodNumber = @prodNumber " +
                             "AND part = @part " +
                             "AND skidNumber = @skidNumber " +
                             "ORDER BY id DESC LIMIT 1";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var updateCommand = new MySqlCommand(updateQuery, connection))
            {
                updateCommand.Parameters.AddWithValue("@prodNumber", prodNumber);
                updateCommand.Parameters.AddWithValue("@part", part);
                updateCommand.Parameters.AddWithValue("@skidNumber", skidNumber);
                updateCommand.Parameters.AddWithValue("@pcs", pcs);
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
         id, 
         timestamp, 
         prodNumber, 
         run, 
         part, 
         component, 
         endDateTime, 
         operator, 
         machine, 
         pcsStart, 
         pcsEnd, 
         notes, 
         skidNumber,
         startDateTime
    FROM pressrun
    WHERE open = 1 AND skidNumber > 0
)
UNION ALL
(
    SELECT 
         id, 
         timestamp, 
         prodNumber, 
         '' AS run,           -- assembly table has no run; default to empty string
         part, 
         '' AS component,     -- no component in assembly; default to empty string
         endDateTime, 
         operator, 
         '' AS machine,       -- no machine info; default to empty string
         0 AS pcsStart,       -- default value for pcsStart
         pcs AS pcsEnd,       -- assembly's pcs value goes to pcsEnd
         '' AS notes,         -- no notes provided; default to empty string
         skidNumber,
         endDateTime AS startDateTime   -- use endDateTime for ordering if no startDateTime exists
    FROM assembly
    WHERE open = 1
)
ORDER BY startDateTime DESC";

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
