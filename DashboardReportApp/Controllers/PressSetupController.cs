using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace DashboardReportApp.Controllers
{
    public class PressSetupController : Controller
    {
        private readonly string _connectionStringMySQL = "server=192.168.1.6;database=sintergy;user=admin;password=N0mad2019";

        [HttpGet]
        [HttpGet]
        public IActionResult Index()
        {
            ViewData["Title"] = "Press Setup";
            ViewData["Operators"] = GetOperators();
            ViewData["Machines"] = GetEquipment();
            ViewData["Trainers"] = GetTrainers(); // Fetch trainers for "Assisted By"
            ViewData["ActiveLogins"] = GetActiveLogins();
            return View();
        }

        private List<string> GetTrainers()
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


        [HttpPost]
        public async Task<IActionResult> Login(string partNumber, string operatorName, string machine)
        {
            try
            {
                using (var connection = new MySqlConnection(_connectionStringMySQL))
                {
                    await connection.OpenAsync();

                    string query = @"INSERT INTO presssetup (part, operator, machine, startDateTime) 
                                     VALUES (@part, @operator, @machine, @startDateTime)";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@part", partNumber.ToUpper());
                        command.Parameters.AddWithValue("@operator", operatorName);
                        command.Parameters.AddWithValue("@machine", machine);
                        command.Parameters.AddWithValue("@startDateTime", DateTime.Now);

                        await command.ExecuteNonQueryAsync();
                    }
                }

                ViewData["Message"] = "Login successfully recorded!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ViewData["Error"] = $"An error occurred: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [HttpPost]
        public async Task<IActionResult> Logout(string partNumber, DateTime startDateTime, string difficulty, string assistanceRequired, string assistedBy, string setupComplete, string notes)
        {
            try
            {
                using (var connection = new MySqlConnection(_connectionStringMySQL))
                {
                    await connection.OpenAsync();

                    // Query to update the logout details
                    string query = @"UPDATE presssetup 
                             SET endDateTime = @endDateTime, 
                                 difficulty = @difficulty, 
                                 assistanceReq = @assistanceReq, 
                                 assistedBy = @assistedBy, 
                                 setupComp = @setupComp, 
                                 notes = @notes 
                             WHERE part = @part AND startDateTime = @startDateTime";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@endDateTime", DateTime.Now);
                        command.Parameters.AddWithValue("@difficulty", difficulty);
                        command.Parameters.AddWithValue("@assistanceReq", assistanceRequired);
                        command.Parameters.AddWithValue("@assistedBy", assistanceRequired == "Assisted" ? assistedBy : null);
                        command.Parameters.AddWithValue("@setupComp", setupComplete);
                        command.Parameters.AddWithValue("@notes", notes);
                        command.Parameters.AddWithValue("@part", partNumber);
                        command.Parameters.AddWithValue("@startDateTime", startDateTime);

                        await command.ExecuteNonQueryAsync();
                    }
                }

                ViewData["Message"] = "Logout successfully recorded!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ViewData["Error"] = $"An error occurred: {ex.Message}";
                return RedirectToAction("Index");
            }
        }


        private List<string> GetOperators()
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

        private List<string> GetEquipment()
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

        private List<dynamic> GetActiveLogins()
        {
            var activeLogins = new List<dynamic>();
            string query = "SELECT part, operator, machine, startDateTime FROM presssetup WHERE endDateTime IS NULL";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            using (var command = new MySqlCommand(query, connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        activeLogins.Add(new
                        {
                            Part = reader["part"].ToString(),
                            Operator = reader["operator"].ToString(),
                            Machine = reader["machine"].ToString(),
                            StartDateTime = (DateTime)reader["startDateTime"]
                        });
                    }
                }
            }

            return activeLogins;
        }
    }
}
