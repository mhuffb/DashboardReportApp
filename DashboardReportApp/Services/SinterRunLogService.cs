using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using Microsoft.Extensions.Configuration;
using DashboardReportApp.Models;
using System.Data;
using Mysqlx.Crud;

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
            SELECT id, timestamp, operator, prodNumber, run, part, oven, process, startDateTime, endDateTime, notes, open, skidNumber
            FROM " + datatable +
            " ORDER BY id DESC";

    await using var connection = new MySqlConnection(_connectionStringMySQL);
    await connection.OpenAsync();
    await using var command = new MySqlCommand(query, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        // Use IsDBNull to check for null values before calling GetDateTime
        DateTime timestamp = !reader.IsDBNull(reader.GetOrdinal("timestamp"))
                             ? reader.GetDateTime("timestamp")
                             : DateTime.MinValue; // or choose a default value

        DateTime startDateTime = !reader.IsDBNull(reader.GetOrdinal("startDateTime"))
                                 ? reader.GetDateTime("startDateTime")
                                 : DateTime.MinValue; // adjust default as needed

        // For endDateTime, you already check for DBNull
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
            Machine = reader["oven"]?.ToString(),
            Process = reader["process"]?.ToString(),
            StartDateTime = startDateTime,
            EndDateTime = endDateTime,
            Notes = reader["notes"]?.ToString(),
            Open = reader["open"] != DBNull.Value ? Convert.ToSByte(reader["open"]) : (sbyte)0,
            SkidNumber = reader["skidNumber"] != DBNull.Value ? reader.GetInt32("skidNumber") : 0
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


    // Close a specific skid for a part and furnace, now including skidNumber
    public void LogoutOfSkid(string part, string run, string skidNumber)
    {
        string query = "UPDATE " + datatable + " SET endDateTime = NOW() " +
                       "WHERE part = @part AND run = @run AND skidNumber = @skidNumber";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@part", part);
                command.Parameters.AddWithValue("@run", run);
                command.Parameters.AddWithValue("@skidNumber", skidNumber);

                int rowsAffected = command.ExecuteNonQuery();
                Console.WriteLine($"✅ Rows Updated: {rowsAffected}");
            }
        }
    }

    // End the current skid record by updating endDateTime, pcs, and notes,
    // and matching on prodNumber, run, part, and skidNumber.
    public void EndSkid(string prodNumber, string part, string skidNumber, string pcs,
                      string run, string oper, string oven, string process, string notes)
    {
        string updateQuery = "UPDATE " + datatable + " " +
                             "SET endDateTime = NOW(), pcs = @pcs, notes = @notes " +
                             "WHERE prodNumber = @prodNumber " +
                             "AND run = @run " +
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
                updateCommand.Parameters.AddWithValue("@run", run);
                updateCommand.Parameters.AddWithValue("@skidNumber", skidNumber);
                updateCommand.Parameters.AddWithValue("@pcs", pcs);
                updateCommand.Parameters.AddWithValue("@notes", notes);

                int rowsAffected = updateCommand.ExecuteNonQuery();
                Console.WriteLine($"✅ Rows Updated: {rowsAffected}");
            }
        }

        string updateQuery2 = "UPDATE pressrun "  +
                             "SET open = 0 " +
                             "WHERE prodNumber = @prodNumber " +
                             "AND run = @run " +
                             "AND part = @part " +
                             "AND skidNumber = @skidNumber " +
                             "ORDER BY id DESC LIMIT 1";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var updateCommand = new MySqlCommand(updateQuery2, connection))
            {
                updateCommand.Parameters.AddWithValue("@prodNumber", prodNumber);
                updateCommand.Parameters.AddWithValue("@part", part);
                updateCommand.Parameters.AddWithValue("@run", run);
                updateCommand.Parameters.AddWithValue("@skidNumber", skidNumber);

                int rowsAffected = updateCommand.ExecuteNonQuery();
                Console.WriteLine($"✅ Rows Updated: {rowsAffected}");
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

        string query = "INSERT INTO " + datatable + " (prodNumber,  run, part, startDateTime, operator, oven, process, notes, skidNumber ) " +
                   "VALUES (@prodNumber, @run, @part,  @startDateTime, @operator, @oven, @process, @notes, @skidNumber)";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {

                command.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                command.Parameters.AddWithValue("@run", model.Run);
                command.Parameters.AddWithValue("@part", model.Part.ToUpper());
                command.Parameters.AddWithValue("@startDateTime", DateTime.Now);
                command.Parameters.AddWithValue("@operator", model.Operator);
                command.Parameters.AddWithValue("@oven", model.Machine);
                command.Parameters.AddWithValue("@process", model.Process);
                command.Parameters.AddWithValue("@notes", string.IsNullOrEmpty(model.Notes) ? DBNull.Value : model.Notes);
                command.Parameters.AddWithValue("@skidNumber", model.SkidNumber);

                command.ExecuteNonQuery();
            }
        }

    }
   
    public async Task<List<PressRunLogModel>> GetOpenGreenSkidsAsync()
    {
        var openGreenSkids = new List<PressRunLogModel>();

        string query = @"
        SELECT id, timestamp, prodNumber, run, part, endDateTime, operator, machine, pcsStart, pcsEnd, notes, skidNumber
        FROM pressrun
        WHERE open = 1 AND skidNumber > 0
        ORDER BY startDateTime DESC";

        await using var connection = new MySqlConnection(_connectionStringMySQL);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            openGreenSkids.Add(new PressRunLogModel
            {
                Id = reader.GetInt32("id"),
                Timestamp = reader.GetDateTime("timestamp"),
                ProdNumber = reader["prodNumber"]?.ToString() ?? "N/A",
                Run = reader["run"]?.ToString() ?? "N/A",
                Part = reader["part"]?.ToString() ?? "N/A",
                EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime"))
              ? null
              : reader.GetDateTime("endDateTime"),

                Operator = reader["operator"]?.ToString() ?? "N/A",
                SkidNumber = reader.GetInt32("skidNumber"),
                Machine = reader["Machine"]?.ToString() ?? "N/A",
                PcsStart = reader.IsDBNull(reader.GetOrdinal("pcsStart")) ? 0 : Convert.ToInt32(reader["pcsStart"]),
                PcsEnd = reader.IsDBNull(reader.GetOrdinal("pcsEnd")) ? 0 : Convert.ToInt32(reader["pcsEnd"])
            });

        }

        return openGreenSkids;
    }

}
