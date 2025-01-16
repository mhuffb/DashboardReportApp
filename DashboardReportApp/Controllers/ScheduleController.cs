using DashboardReportApp.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System.Data.Odbc;

namespace DashboardReportApp.Controllers
{
    public class ScheduleController : Controller
    {
        private static string connectionStringMySQL = "server=192.168.1.6;database=sintergy;user=admin;password=N0mad2019";
        private static string connectionStringDataflex = @"Dsn=Dataflex;dbq=V:\VDF\Sintergy\Data;displayloginbox=No;yeardigits=4;displayrecnum=Yes;readonly=Yes;returnemptystringsasnulls=No;useodbccompatibility=Yes;usesimulatedtransactions=Yes;converttolongvarchar=No;server=FlexODBC";

        public IActionResult Index(string masterId = null, int quantity = 0)
        {
            var viewModel = new ScheduleViewModel
            {
                AllComponents = new List<SintergyComponent>(),
                OpenParts = GetOpenParts()
            };

            if (!string.IsNullOrEmpty(masterId) && quantity > 0)
            {
                viewModel.AllComponents = GetComponentsForMasterId(masterId.ToUpper(), quantity);
            }

            Console.WriteLine($"Components Count: {viewModel.AllComponents.Count}");

            return View(viewModel);
        }


        private List<SintergyComponent> GetComponentsForMasterId(string masterId, int quantity)
        {
            var allComponents = new List<SintergyComponent>();
            var visitedMasterIds = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(masterId);

            int nextRunNumber = GetNextRunNumber(); // Fetch the initial run number
            int nextSinterGroup = GetNextSinterGroupNumber(); // Fetch the next sintergroup number

            while (queue.Count > 0)
            {
                string currentMasterId = queue.Dequeue();

                if (visitedMasterIds.Contains(currentMasterId))
                    continue;

                visitedMasterIds.Add(currentMasterId);

                using (var connection = new OdbcConnection(connectionStringDataflex))
                {
                    string query = @"
                SELECT masteras.master_id, masteras.cmaster_id, masteras.qty, masteras.line_no 
                FROM masteras 
                WHERE masteras.master_id = ? 
                ORDER BY masteras.master_id, masteras.line_no";

                    var command = new OdbcCommand(query, connection);
                    command.Parameters.AddWithValue("?", currentMasterId);

                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string componentName = reader["cmaster_id"].ToString();

                            var component = new SintergyComponent
                            {
                                MasterId = reader["master_id"].ToString(),
                                Component = componentName,
                                QtyToMakeMasterID = Convert.ToInt32(reader["qty"]),
                                QtyToSchedule = quantity * Convert.ToInt32(reader["qty"]),
                                Run = nextRunNumber++.ToString(), // Assign and increment run number
                                SinterGroup = componentName.Contains("C") ? nextSinterGroup++ : (int?)null // Convert to nullable int
                            };

                            allComponents.Add(component);

                            if (!string.IsNullOrWhiteSpace(component.Component) && !visitedMasterIds.Contains(component.Component))
                            {
                                queue.Enqueue(component.Component);
                            }
                        }
                    }
                }
            }

            // If no components were found, add the master part itself
            if (!allComponents.Any())
            {
                allComponents.Add(new SintergyComponent
                {
                    MasterId = masterId,
                    Component = string.Empty, // No sub-component
                    QtyToMakeMasterID = 1, // Default quantity to make for the master part
                    QtyToSchedule = quantity, // Use the provided quantity
                    Run = nextRunNumber++.ToString(), // Assign and increment run number
                    SinterGroup = null // No sintergroup since it's not a component
                });
            }

            return allComponents;
        }



        private List<SintergyComponent> GetOpenParts()
        {
            var openParts = new List<SintergyComponent>();
            string query = "SELECT date, part, quantity, run, sintergroup FROM schedule WHERE open = 1";

            using (var connection = new MySqlConnection(connectionStringMySQL))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var component = new SintergyComponent
                            {
                                Date = reader["date"] as DateTime?, // Nullable DateTime
                                MasterId = reader["part"].ToString(),
                                QtyToSchedule = Convert.ToInt32(reader["quantity"]),
                                Run = reader["run"].ToString(),
                                SinterGroup = reader["sintergroup"] == DBNull.Value
                        ? (int?)null
                        : Convert.ToInt32(reader["sintergroup"]) // Nullable int
                            };
                            openParts.Add(component);
                        }
                    }
                }
            }

            return openParts;
        }
        [HttpPost]
        public IActionResult ScheduleComponents(ScheduleViewModel viewModel)
        {
            if (viewModel.AllComponents == null || !viewModel.AllComponents.Any())
            {
                TempData["Error"] = "No components to save.";
                return RedirectToAction("Index");
            }

            using (var connection = new MySqlConnection(connectionStringMySQL))
            {
                connection.Open();
                foreach (var component in viewModel.AllComponents)
                {
                    string insertQuery = @"
                INSERT INTO schedule (part, component, quantity, run, sintergroup, date)
                VALUES (@part, @component, @quantity, @run, @sintergroup, @date)";

                    using (var command = new MySqlCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@part", component.MasterId);
                        command.Parameters.AddWithValue("@component", component.Component);
                        command.Parameters.AddWithValue("@quantity", component.QtyToSchedule);
                        command.Parameters.AddWithValue("@run", component.Run);

                        // Handle null for SinterGroup
                        if (component.SinterGroup.HasValue)
                        {
                            command.Parameters.AddWithValue("@sintergroup", component.SinterGroup.Value);
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@sintergroup", DBNull.Value);
                        }

                        command.Parameters.AddWithValue("@date", DateTime.Now);

                        command.ExecuteNonQuery();
                    }
                }
            }

            TempData["Success"] = "Components scheduled successfully!";
            return RedirectToAction("Index");
        }



        private int GetNextRunNumber()
        {
            int nextRunNumber = 0;

            using (var connection = new MySqlConnection(connectionStringMySQL))
            {
                string query = "SELECT MAX(run) + 1 AS NextRun FROM schedule";
                connection.Open();

                using (var command = new MySqlCommand(query, connection))
                {
                    var result = command.ExecuteScalar();
                    nextRunNumber = result != DBNull.Value ? Convert.ToInt32(result) : 1; // Default to 1 if no runs exist
                }
            }

            return nextRunNumber;
        }
        private int GetNextSinterGroupNumber()
        {
            int nextSinterGroup = 0;

            using (var connection = new MySqlConnection(connectionStringMySQL))
            {
                string query = "SELECT MAX(sintergroup) + 1 AS NextSinterGroup FROM schedule";
                connection.Open();

                using (var command = new MySqlCommand(query, connection))
                {
                    var result = command.ExecuteScalar();
                    nextSinterGroup = result != DBNull.Value ? Convert.ToInt32(result) : 1; // Default to 1 if no sintergroup exists
                }
            }

            return nextSinterGroup;
        }

    }
}
