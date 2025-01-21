using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using Microsoft.Extensions.Configuration;
using DashboardReportApp.Models;

public class SinterRunLogService
{
    private readonly string _connectionStringMySQL;
    private readonly string _connectionStringDataflex;

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
        string query = "SELECT id, timestamp, operator, part, oven, process, startDateTime, endDateTime, notes, run FROM sinterrun WHERE endDateTime IS NULL";

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
                            Id = reader["id"].ToString(),
                            Timestamp = reader["timestamp"].ToString(),
                            Operator = reader["operator"].ToString(),
                            Part = reader["part"].ToString(),
                            Furnace = reader["oven"].ToString(),
                            Process = reader["process"].ToString(),
                            StartDateTime = reader["startDateTime"].ToString(),
                            EndDateTime = reader["endDateTime"] as string,
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
        string query = "UPDATE sinterrun SET endDateTime = @endDateTime WHERE oven = @furnace AND endDateTime IS NULL";

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
    public void CloseSkid(string part, string furnace)
    {
        string query = "UPDATE sinterrun SET endDateTime = @endDateTime WHERE part = @part AND oven = @furnace AND endDateTime IS NULL";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@endDateTime", DateTime.Now);
                command.Parameters.AddWithValue("@part", part);
                command.Parameters.AddWithValue("@furnace", furnace);
                command.ExecuteNonQuery();
            }
        }
    }

    // End skids on the same furnace if one is already running
    public void EndSkidsByFurnaceIfNeeded(string furnace)
    {
        string query = "UPDATE sinterrun SET endDateTime = @endDateTime WHERE oven = @furnace AND endDateTime IS NULL";

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
    public void StartSkid(string operatorName, string part, string furnace, string process, string notes)
    {
        string query = "INSERT INTO sinterrun (operator, part, startDateTime, oven, process, notes) VALUES (@operator, @part, @startDateTime, @oven, @process, @notes)";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@operator", operatorName);
                command.Parameters.AddWithValue("@part", part.ToUpper());
                command.Parameters.AddWithValue("@startDateTime", DateTime.Now);
                command.Parameters.AddWithValue("@oven", furnace);
                command.Parameters.AddWithValue("@process", process);
                command.Parameters.AddWithValue("@notes", string.IsNullOrEmpty(notes) ? DBNull.Value : notes);

                command.ExecuteNonQuery();
            }
        }
    }

}
