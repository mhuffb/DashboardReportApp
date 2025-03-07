using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using Microsoft.Extensions.Configuration;
using DashboardReportApp.Models;
using System.Data;
using Mysqlx.Crud;

public class AssemblyService
{
    private readonly string _connectionStringMySQL;
    private readonly string _connectionStringDataflex;
    private string datatable = "assembly";

    public AssemblyService(IConfiguration configuration)
    {
        _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
        _connectionStringDataflex = configuration.GetConnectionString("DataflexConnection");
    }
    /// <summary>
    /// Get *all* runs in descending order by startDateTime.
    /// </summary>
   public async Task<List<AssemblyModel>> GetAllRunsAsync()
{
    var allRuns = new List<AssemblyModel>();

    string query = @"
            SELECT id, timestamp, operator, prodNumber, part, endDateTime, notes, open, skidNumber, pcs
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

        DateTime endDateTime = !reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                                 ? reader.GetDateTime("endDateTime")
                                 : DateTime.MinValue; // adjust default as needed


            allRuns.Add(new AssemblyModel
            {
                Id = reader.GetInt32("Id"),
                Operator = reader["operator"]?.ToString(),
                ProdNumber = reader["prodNumber"]?.ToString() ?? "N/A",
                Part = reader["part"]?.ToString() ?? "N/A",
                EndDateTime = endDateTime,
                Notes = reader["notes"]?.ToString(),
                Open = reader["open"] != DBNull.Value ? Convert.ToSByte(reader["open"]) : (sbyte)0,
                SkidNumber = reader["skidNumber"] != DBNull.Value ? reader.GetInt32("skidNumber") : 0,
                Pcs = !reader.IsDBNull(reader.GetOrdinal("pcs")) ? reader.GetInt32("pcs") : 0


            });
    }

    return allRuns;
}
    public async Task<List<PressRunLogModel>> GetOpenGreenSkidsAsync()
    {
        var openGreenSkids = new List<PressRunLogModel>();
        var partsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string query = @"
SELECT id, timestamp, prodNumber, run, part, component, endDateTime, operator, machine, pcsStart, pcsEnd, notes, skidNumber
FROM pressrun
WHERE open = 1 AND skidNumber > 0
ORDER BY startDateTime DESC";

        await using var connection = new MySqlConnection(_connectionStringMySQL);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            // Retrieve the component value.
            string component = reader["component"]?.ToString() ?? "N/A";

            // Only process if component contains "C" (case-insensitive)
            if (string.IsNullOrEmpty(component) || component.IndexOf("C", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            // Retrieve the part value.
            string part = reader["part"]?.ToString() ?? "N/A";

            // Only add the record if this part hasn't been seen yet.
            if (partsSeen.Contains(part))
            {
                continue;
            }
            partsSeen.Add(part);

            openGreenSkids.Add(new PressRunLogModel
            {
                Id = reader.GetInt32("id"),
                Timestamp = reader.GetDateTime("timestamp"),
                ProdNumber = reader["prodNumber"]?.ToString() ?? "N/A",
                Run = reader["run"]?.ToString() ?? "N/A",
                Part = part,
                Component = component,
                EndDateTime = reader.IsDBNull(reader.GetOrdinal("endDateTime"))
                              ? null
                              : reader.GetDateTime("endDateTime"),
                Operator = reader["operator"]?.ToString() ?? "N/A",
                SkidNumber = reader.GetInt32("skidNumber"),
                Machine = reader["machine"]?.ToString() ?? "N/A",
                PcsStart = reader.IsDBNull(reader.GetOrdinal("pcsStart"))
                              ? 0
                              : Convert.ToInt32(reader["pcsStart"]),
                PcsEnd = reader.IsDBNull(reader.GetOrdinal("pcsEnd"))
                              ? 0
                              : Convert.ToInt32(reader["pcsEnd"])
            });
        }

        return openGreenSkids;
    }
    // Get a list of operators from MySQL
    public List<string> GetOperators()
    {
        var operators = new List<string>();
        string query = "SELECT name FROM operators WHERE dept = 'greenAssembly' ORDER BY name";

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
    // Start a new skid (insert into AssemblyModel table)
    public void LogSkid(AssemblyModel model)
    {
        int nextSkidNumber = 1;
        // Query to determine the current maximum skid number for the given prodNumber and part.
        string selectQuery = "SELECT IFNULL(MAX(skidNumber), 0) FROM " + datatable + " WHERE prodNumber = @prodNumber AND part = @part";

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();

            // Determine the next skid number.
            using (var selectCommand = new MySqlCommand(selectQuery, connection))
            {
                selectCommand.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                selectCommand.Parameters.AddWithValue("@part", model.Part.ToUpper());
                var result = selectCommand.ExecuteScalar();
                if (result != null)
                {
                    int currentMax = Convert.ToInt32(result);
                    nextSkidNumber = currentMax + 1;
                }
            }

            // Insert the new skid record with open=1
            string insertQuery = @"
    INSERT INTO " + datatable + @" 
        (prodNumber, part, endDateTime, operator, notes, skidNumber, pcs, open) 
    VALUES 
        (@prodNumber, @part, @endDateTime, @operator, @notes, @skidNumber, @pcs, 1)";

            using (var command = new MySqlCommand(insertQuery, connection))
            {
                command.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                command.Parameters.AddWithValue("@part", model.Part.ToUpper());
                command.Parameters.AddWithValue("@endDateTime", DateTime.Now);
                command.Parameters.AddWithValue("@operator", model.Operator);
                command.Parameters.AddWithValue("@notes",
                    string.IsNullOrEmpty(model.Notes) ? DBNull.Value : (object)model.Notes
                );
                command.Parameters.AddWithValue("@skidNumber", nextSkidNumber);
                command.Parameters.AddWithValue("@pcs", model.Pcs);

                command.ExecuteNonQuery();
            }

        }
    }





    public void EndProduction(string part, string prodNumber)
    {

        string updateQuery1 = "UPDATE pressrun " +
                       "SET open = 0 " +
                       "WHERE prodNumber = @prodNumber AND part = @part";

        ;

        using (var connection = new MySqlConnection(_connectionStringMySQL))
        {
            connection.Open();
            using (var updateCommand = new MySqlCommand(updateQuery1, connection))
            {
                updateCommand.Parameters.AddWithValue("@part", part);
                updateCommand.Parameters.AddWithValue("@prodNumber", prodNumber);

                int rowsAffected = updateCommand.ExecuteNonQuery();
                Console.WriteLine($"✅ Rows Updated: {rowsAffected}");
            }
        }

       

    }
   

}
