using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using DashboardReportApp.Services;
using DashboardReportApp.Models;

public class PressRunLogService
{
    private readonly string _connectionString = "server=192.168.1.6;database=sintergy;user=admin;password=N0mad2019";

    public async Task<PressRunLogViewModel> GetPressRunLogViewModelAsync()
    {
        var viewModel = new PressRunLogViewModel
        {
            OperatorList = await GetOperatorsAsync(),
            EquipmentList = await GetEquipmentAsync(),
            LoggedInRuns = await GetLoggedInRunsAsync()
        };

        return viewModel;
    }

    private async Task<List<LoggedInRunModel>> GetLoggedInRunsAsync()
    {
        var loggedInRuns = new List<LoggedInRunModel>();

        const string query = @"SELECT operator, machine, part, startDateTime 
                           FROM pressrun 
                           WHERE endDateTime IS NULL";

        await using var connection = new MySqlConnection(_connectionString);
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

        await using var connection = new MySqlConnection(_connectionString);
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

        await using var connection = new MySqlConnection(_connectionString);
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
        const string query = @"INSERT INTO pressrun (operator, part, machine, run, startDateTime, pcsStart)
                           VALUES (@operator, @part, @machine, @run, @startDateTime, @pcsStart)";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@operator", formModel.Operator);
        command.Parameters.AddWithValue("@part", formModel.Part);
        command.Parameters.AddWithValue("@machine", formModel.Machine);
        command.Parameters.AddWithValue("@run", formModel.Run);
        command.Parameters.AddWithValue("@startDateTime", DateTime.Now); // Capture login time
        command.Parameters.AddWithValue("@pcsStart", formModel.PcsStart);
        await command.ExecuteNonQueryAsync();
    }


    public async Task HandleLogoutAsync(PressRunLogFormModel formModel)
    {
        const string query = @"UPDATE pressrun
                           SET endDateTime = @endDateTime, pcsEnd = @pcsEnd, scrap = @scrap, notes = @notes
                           WHERE part = @part AND startDateTime = @startDateTime";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@endDateTime", DateTime.Now); // Capture logout time
        command.Parameters.AddWithValue("@pcsEnd", formModel.PcsEnd);
        command.Parameters.AddWithValue("@scrap", formModel.Scrap);
        command.Parameters.AddWithValue("@notes", formModel.Notes ?? string.Empty);
        command.Parameters.AddWithValue("@part", formModel.Part);
        command.Parameters.AddWithValue("@startDateTime", formModel.StartDateTime);
        await command.ExecuteNonQueryAsync();
    }

}
