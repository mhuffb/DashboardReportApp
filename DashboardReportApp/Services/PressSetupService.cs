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
        private readonly string _connectionString = "server=192.168.1.6;database=sintergy;user=admin;password=N0mad2019";

        public List<PressSetupModel> GetAllRecords(string part, string operatorName, string machine, string setupComplete,
                                                   string assistanceRequired, string search, string startDate,
                                                   string endDate, string sortBy, string sortOrder)
        {
            var records = new List<PressSetupModel>();
            string query = "SELECT * FROM presssetup order by id desc";


            using (var connection = new MySqlConnection(_connectionString))
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
            using (var connection = new MySqlConnection(_connectionString))
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
                                      string assistedBy, string setupComplete, string notes)
        {
            using (var connection = new MySqlConnection(_connectionString))
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

            using (var connection = new MySqlConnection(_connectionString))
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

            using (var connection = new MySqlConnection(_connectionString))
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

            using (var connection = new MySqlConnection(_connectionString))
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
        public string GetRunForPart(string part)
        {
            string run = "";
            string query = "SELECT run FROM schedule WHERE part = @part AND open = 1";

            using (var connection = new MySqlConnection(_connectionString))
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@part", part);
                connection.Open();
                var result = command.ExecuteScalar();
                if (result != null)
                {
                    run = result.ToString();
                }
            }
            return run;
        }

       
        public List<Scheduled> GetScheduledParts()
        {
            var records = new List<Scheduled>();
            string query = "SELECT * FROM schedule where open = 1 order by id desc";


            using (var connection = new MySqlConnection(_connectionString))
            using (var command = new MySqlCommand(query, connection))
            {

                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(new Scheduled
                        {
                            Id = Convert.ToInt32(reader["id"]),
                            Part = reader["part"].ToString(),
                            Component = reader["component"].ToString(),
                            Subcomponent = reader["Subcomponent"].ToString(),
                            ProdNumber = reader["ProdNumber"].ToString(),
                            Run = reader["run"].ToString(),


                        });
                    }
                }
            }
            return records;
        }

    }
}
