using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using DashboardReportApp.Services;

public class PressRunLogService
{
    private readonly string _connectionStringMySQL;
    private readonly string _connectionStringDataflex;

    public PressRunLogService(IConfiguration configuration)
    {
        _connectionStringMySQL = configuration.GetConnectionString("MySQLConnection");
        //_connectionStringDataflex = configuration.GetConnectionString("DataflexConnection");
    }
    public async Task<PressRunLogViewModel> GetPressRunLogViewModelAsync()
    {
        return new PressRunLogViewModel
        {
            LoggedInRuns = await GetLoggedInRunsAsync(),
            OperatorList = await GetOperatorsAsync(),
            EquipmentList = await GetEquipmentAsync() // Machines
        };
    }


    private async Task<List<LoggedInRunModel>> GetLoggedInRunsAsync()
    {
        var loggedInRuns = new List<LoggedInRunModel>();

        const string query = @"SELECT operator, machine, part, startDateTime 
                           FROM pressrun 
                           WHERE endDateTime IS NULL";

        await using var connection = new MySqlConnection(_connectionStringMySQL);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            loggedInRuns.Add(new LoggedInRunModel
            {
                Operator = reader.GetString("operator"),
                Machine = reader.GetString("machine"),
                Part = reader.IsDBNull(reader.GetOrdinal("part")) ? "N/A" : reader.GetString("part"),
                StartDateTime = reader.GetDateTime("startDateTime")
            });
        }

        return loggedInRuns;
    }


    private async Task<List<string>> GetEquipmentAsync()
    {
        var equipment = new List<string>();
        const string query = "SELECT equipment FROM equipment WHERE name = 'press' AND (department = 'molding' OR department = 'sizing') AND (status IS NULL OR status != 'obsolete') ORDER BY equipment";

        await using var connection = new MySqlConnection(_connectionStringMySQL);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // Safely retrieve equipment and convert to string
            equipment.Add(reader["equipment"]?.ToString());
        }

        return equipment;
    }


    private async Task<List<string>> GetOperatorsAsync()
    {
        var operators = new List<string>();
        const string query = "SELECT name FROM operators WHERE dept = 'molding' ORDER BY name";

        await using var connection = new MySqlConnection(_connectionStringMySQL);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            operators.Add(reader.GetString("name"));
        }

        return operators;
    }

    public async Task HandleLoginAsync(PressRunLogFormModel formModel)
    {
        const string query = @"
        INSERT INTO pressrun (operator, part, machine, startDateTime)
        VALUES (@operator, @part, @machine, @startDateTime)";

        await using var connection = new MySqlConnection(_connectionStringMySQL);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);

        command.Parameters.AddWithValue("@operator", formModel.Operator);
        command.Parameters.AddWithValue("@part", formModel.Part);
        command.Parameters.AddWithValue("@machine", formModel.Machine);
        command.Parameters.AddWithValue("@startDateTime", formModel.StartDateTime);

        await command.ExecuteNonQueryAsync();
    }



    public async Task HandleLogoutAsync(PressRunLogFormModel formModel)
    {
        const string query = @"
        UPDATE pressrun
        SET endDateTime = @endDateTime, scrap = @scrap, notes = @notes
        WHERE part = @part AND startDateTime = @startDateTime";

        await using var connection = new MySqlConnection(_connectionStringMySQL);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);

        command.Parameters.AddWithValue("@endDateTime", formModel.EndDateTime);
        command.Parameters.AddWithValue("@scrap", formModel.Scrap);
        command.Parameters.AddWithValue("@notes", formModel.Notes ?? string.Empty); // Handle optional notes
        command.Parameters.AddWithValue("@part", formModel.Part);
        command.Parameters.AddWithValue("@startDateTime", formModel.StartDateTime);

        await command.ExecuteNonQueryAsync();
    }


}
