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

        public PressSetupService(IConfiguration config)
        {
            _connectionStringMySQL = config.GetConnectionString("MySQLConnection");
        }

        public List<PressSetupModel> GetAllRecords(string part, string operatorName, string machine, string setupComplete,
                                                   string assistanceRequired, string search, string startDate,
                                                   string endDate, string sortBy, string sortOrder)
        {
            var records = new List<PressSetupModel>();
            string query = "SELECT * FROM presssetup order by id desc";


            using (var connection = new MySqlConnection(_connectionStringMySQL))
            using (var command = new MySqlCommand(query, connection))
            {

                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(new PressSetupModel
                        {
                            Id = Convert.ToInt32(reader["id"]),
                            Timestamp = reader["timestamp"] as DateTime?,
                            Part = reader["part"].ToString(),
                            Operator = reader["operator"].ToString(),
                            StartDateTime = reader["startDateTime"] as DateTime?,
                            EndDateTime = reader["endDateTime"] as DateTime?,
                            Machine = reader["machine"].ToString(),
                            PressType = reader["pressType"].ToString(),
                            Difficulty = reader["difficulty"].ToString(),
                            SetupComp = reader["setupComp"].ToString(),
                            AssistanceReq = reader["assistanceReq"].ToString(),
                            AssistedBy = reader["assistedBy"].ToString(),
                            Notes = reader["notes"].ToString(),
                            Open = reader["open"] != DBNull.Value ? Convert.ToSByte(reader["open"]) : (sbyte)0,
                            Run = reader["run"].ToString(),
                            ProdNumber = reader["prodNumber"].ToString()
                        });
                    }
                }
            }
            return records;
        }

        public async Task LoginAsync(PressSetupLoginViewModel model)
        {
            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                await connection.OpenAsync();
                string query = @"INSERT INTO presssetup (part, run, operator, machine, startDateTime, open, prodNumber) 
                         VALUES (@part, @run, @operator, @machine, @startDateTime, @open, @prodNumber)";

                using (var command = new MySqlCommand(query, connection))
                {
                    string toAdd = null;
                    if (model.Part != null)
                    {
                        toAdd = model.Part; ;
                    }
                    if (model.Component != null)
                    {
                        toAdd = model.Component;
                    }
                    if (model.Subcomponent != null)
                    {
                        toAdd = model.Subcomponent;
                    }
                    command.Parameters.AddWithValue("@part", toAdd.ToUpper());
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
                                 WHERE part = @part AND startDateTime = @startDateTime";

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
                        var subcomponent = reader["Subcomponent"]?.ToString(); // may be null or empty

                        // If subcomponent exists and contains "PC" or "Y", skip this record.
                        if (!string.IsNullOrWhiteSpace(subcomponent))
                        {
                            if (subcomponent.IndexOf("PC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                subcomponent.IndexOf("Y", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            // If subcomponent is null/empty and component contains "Y", skip.
                            if (!string.IsNullOrWhiteSpace(component) &&
                                component.IndexOf("Y", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                continue;
                            }
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
                            Subcomponent = subcomponent,
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

    }
}
