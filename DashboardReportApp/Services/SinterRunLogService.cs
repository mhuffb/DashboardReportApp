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
    private string datatable = "sinterruntest";

    public SinterRunLogService(IConfiguration configuration)
    {
        _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
        _connectionStringDataflex = configuration.GetConnectionString("DataflexConnection");
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

    // Fetch open skids from MySQL
    public List<SinterRunSkid> GetOpenSkids()
    {
        var openSkids = new List<SinterRunSkid>();
        string query = "SELECT id, timestamp, operator, part, oven, process, startDateTime, endDateTime, notes, run FROM " + datatable + " WHERE endDateTime IS NULL";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var skid = new SinterRunSkid
                        {
                            Id = reader.GetInt32("id"),
                            Timestamp = reader.GetDateTime("timestamp"),
                            Operator = reader["operator"].ToString(),
                            Part = reader["part"].ToString(),
                            Machine = reader["oven"].ToString(),
                            Process = reader["process"].ToString(),
                            StartDateTime = reader.GetDateTime("startDateTime"),
                            EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                                   ? null : reader.GetDateTime("endDateTime"),
                            Notes = reader["notes"] as string,
                            Run = reader["run"] as string
                        };

                        openSkids.Add(skid);
                    }
                }
            }
        }

        return openSkids;
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



    // Get part numbers from Dataflex
    public List<string> GetParts()
    {
        var parts = new List<string>();
        string query = "SELECT DISTINCT master_id FROM master WHERE active_status = 'A' AND master_id NOT LIKE '%p%' ORDER BY master_id";

        using (var connection = new OdbcConnection(_connectionStringDataflex))
        {
            connection.Open();
            using (var command = new OdbcCommand(query, connection))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        parts.Add(reader["master_id"].ToString());
                    }
                }
            }
        }

        return parts;
    }

  

    // Close all skids for the selected furnace
    public void CloseSkidsByFurnace(string furnace)
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

    // Close a specific skid for a part and furnace
    public void CloseSkid(string part, string run)
    {
        string query = "UPDATE " + datatable + " SET open = 0, endDateTime = NOW() WHERE part = @part AND run = @run AND open = 1";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@part", part);
                command.Parameters.AddWithValue("@run", run);

                int rowsAffected = command.ExecuteNonQuery();
                Console.WriteLine($"✅ Rows Updated: {rowsAffected}");
            }
        }
    }

    // End skids on the same furnace if one is already running
    public void EndSkidsByFurnaceIfNeeded(string furnace)
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
    public void StartSkid(string operatorName, string part, string run, string furnace, string process, string notes)
    {
        Console.WriteLine("📌 StartSkid() Called:");
        Console.WriteLine($"➡️ Operator: {operatorName}");
        Console.WriteLine($"➡️ Part: {part}");
        Console.WriteLine($"➡️ Run: {run}");  // Check if this prints correctly
        Console.WriteLine($"➡️ Furnace: {furnace}");
        Console.WriteLine($"➡️ Process: {process}");
        Console.WriteLine($"➡️ Notes: {notes}");

        string query = "INSERT INTO " + datatable + " (operator, part, run, startDateTime, oven, process, notes, open) VALUES (@operator, @part, @run, @startDateTime, @oven, @process, @notes, @open)";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@operator", operatorName);
                command.Parameters.AddWithValue("@part", part.ToUpper());
                command.Parameters.AddWithValue("@run", run);
                command.Parameters.AddWithValue("@startDateTime", DateTime.Now);
                command.Parameters.AddWithValue("@oven", furnace);
                command.Parameters.AddWithValue("@process", process);
                command.Parameters.AddWithValue("@notes", string.IsNullOrEmpty(notes) ? DBNull.Value : notes);
                command.Parameters.AddWithValue("@open", 1);

                command.ExecuteNonQuery();
            }
        }

    }
    /// <summary>
    /// Get *all* runs in descending order by startDateTime.
    /// </summary>
    public async Task<List<SinterRunSkid>> GetAllRunsAsync()
    {
        var allRuns = new List<SinterRunSkid>();

        string query = @"
                SELECT id, timestamp, operator, run, part, oven, process, startDateTime, endDateTime, notes, open
                FROM " + datatable + 
                " ORDER BY id DESC";

        await using var connection = new MySqlConnection(_connectionStringMySQL);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            allRuns.Add(new SinterRunSkid
            {
                Id = reader.GetInt32("id"),
                Timestamp = reader.GetDateTime("timestamp"),
                Operator = reader["operator"]?.ToString(),
                Run = reader["run"]?.ToString() ?? "N/A",
                Part = reader["part"]?.ToString() ?? "N/A",
                Machine = reader["oven"]?.ToString(),
                Process = reader["process"]?.ToString(),
                StartDateTime = reader.GetDateTime("startDateTime"),
                EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                               ? null : reader.GetDateTime("endDateTime"),
                Notes = reader["notes"]?.ToString(),
                Open = reader["open"] != DBNull.Value ? Convert.ToSByte(reader["open"]) : (sbyte)0



            });
        }

        return allRuns;
    }
    public async Task<List<SinterRunSkid>> GetOpenRunsAsync()
    {
        var openRuns = new List<SinterRunSkid>();

        string query = @"
        SELECT id, timestamp, run, part
        FROM pressrun" +
            " WHERE open = 1 ORDER BY startDateTime DESC";

    await using var connection = new MySqlConnection(_connectionStringMySQL);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            openRuns.Add(new SinterRunSkid
            {
                Id = reader.GetInt32("id"),
                Timestamp = reader.GetDateTime("timestamp"),
                Run = reader["run"]?.ToString() ?? "N/A",
                Part = reader["part"]?.ToString() ?? "N/A",
                
            });
        }

        return openRuns;
    }

}
