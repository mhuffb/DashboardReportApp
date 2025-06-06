﻿using DashboardReportApp.Models;
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

        public PressSetupService(IConfiguration config, SharedService sharedService, MoldingService moldingService)
        {
            _connectionStringMySQL = config.GetConnectionString("MySQLConnection");
            _sharedService = sharedService;
            _moldingService = moldingService;
        }
        public List<PressSetupModel> GetAllRecords()
        {
            var records = new List<PressSetupModel>();
            records = _moldingService.GetPressSetups();
            return records;
        }

        public async Task LoginAsync(PressSetupLoginViewModel model)
        {
            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                string query = @"INSERT INTO presssetup (part, component, run, operator, machine, startDateTime, open, prodNumber) 
                         VALUES (@part, @component, @run, @operator, @machine, @startDateTime, @open, @prodNumber)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@part", model.Part?.ToUpper() ?? "");
                    command.Parameters.AddWithValue("@component", model.Component?.ToUpper() ?? "");
                    command.Parameters.AddWithValue("@run", model.Run);
                    command.Parameters.AddWithValue("@operator", model.Operator);
                    command.Parameters.AddWithValue("@machine", model.Machine);
                    command.Parameters.AddWithValue("@startDateTime", DateTime.Now);
                    command.Parameters.AddWithValue("@open", 0);
                    command.Parameters.AddWithValue("@prodNumber", model.ProdNumber);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }



        public async Task LogoutAsync(string partNumber, DateTime startDateTime, string difficulty, string assistanceRequired,
                                      string assistedBy, string setupComplete, string notes, string run)
        {
            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                string query = @"UPDATE presssetup 
                                 SET endDateTime = @endDateTime, 
                                     difficulty = @difficulty, 
                                     assistanceReq = @assistanceReq, 
                                     assistedBy = @assistedBy, 
                                     setupComp = @setupComp, 
                                     open = @open,
                                     notes = @notes
                                 WHERE run = @run AND startDateTime = @startDateTime";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@endDateTime", DateTime.Now);
                    command.Parameters.AddWithValue("@difficulty", difficulty);
                    command.Parameters.AddWithValue("@assistanceReq", assistanceRequired);
                    command.Parameters.AddWithValue("@assistedBy", assistanceRequired == "Assisted" ? assistedBy : null);
                    
                    command.Parameters.AddWithValue("@setupComp", setupComplete);
                    if (setupComplete == "Yes")
                    {
                        command.Parameters.AddWithValue("@open", 1);
                        CloseOnScheduleAsync(run);
                    }
                    else
                    {
                        command.Parameters.AddWithValue("@open", 0);
                    }
                    command.Parameters.AddWithValue("@notes", notes);
                    command.Parameters.AddWithValue("@part", partNumber);
                    command.Parameters.AddWithValue("@startDateTime", startDateTime);
                    command.Parameters.AddWithValue("@run", run);

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




    }
}
