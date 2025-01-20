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
        private static string connectionStringDataflex = @"Dsn=Dataflex;dbq=\\SINTERGYDC2024\\Vol1\\VDF\\Sintergy\\Data;displayloginbox=no;yeardigits=4;displayrecnum=Yes;readonly=Yes;returnemptystringsasnulls=No;useodbccompatibility=Yes;usesimulatedtransactions=Yes;converttolongvarchar=No;";
        


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
            var processedPairs = new HashSet<(string Component, string SubComponent)>();
            var visitedMasterIds = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(masterId);

            int nextRunNumber = GetNextRunNumber();
            bool componentsFound = false;

            while (queue.Count > 0)
            {
                string currentMasterId = queue.Dequeue();

                if (visitedMasterIds.Contains(currentMasterId))
                    continue;

                visitedMasterIds.Add(currentMasterId);

                using (var connection = new OdbcConnection(connectionStringDataflex))
                {
                    string componentQuery = @"
                SELECT masteras.master_id, masteras.cmaster_id, masteras.qty 
                FROM masteras 
                WHERE masteras.master_id = ?
                ORDER BY masteras.line_no";

                    var command = new OdbcCommand(componentQuery, connection);
                    command.Parameters.AddWithValue("?", currentMasterId);

                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            componentsFound = true; // Mark that components were found
                            string componentId = reader["cmaster_id"].ToString();
                            int qty = Convert.ToInt32(reader["qty"]);

                            if (!processedPairs.Contains((currentMasterId, componentId)))
                            {
                                allComponents.Add(new SintergyComponent
                                {
                                    MasterId = masterId,
                                    Component = componentId,
                                    SubComponent = null,
                                    QtyToMakeMasterID = qty, // This strictly comes from the database
                                    QtyToSchedule = quantity * qty, // User-defined editable value
                                    Run = nextRunNumber++.ToString()
                                });

                                processedPairs.Add((currentMasterId, componentId));
                                queue.Enqueue(componentId); // Add component for further processing
                            }
                        }
                    }

                    // Process subcomponents for each component
                    var componentsSnapshot = allComponents
                        .Where(c => c.MasterId == masterId && c.SubComponent == null)
                        .ToList();

                    foreach (var component in componentsSnapshot)
                    {
                        string subComponentQuery = @"
                    SELECT masteras.cmaster_id, masteras.qty 
                    FROM masteras 
                    WHERE masteras.master_id = ?";

                        var subCommand = new OdbcCommand(subComponentQuery, connection);
                        subCommand.Parameters.AddWithValue("?", component.Component);

                        using (var subReader = subCommand.ExecuteReader())
                        {
                            while (subReader.Read())
                            {
                                string subComponentId = subReader["cmaster_id"].ToString();
                                int subQty = Convert.ToInt32(subReader["qty"]);

                                if (!processedPairs.Contains((component.Component, subComponentId)))
                                {
                                    allComponents.Add(new SintergyComponent
                                    {
                                        MasterId = masterId,
                                        Component = component.Component,
                                        SubComponent = subComponentId,
                                        QtyToMakeMasterID = subQty, // This strictly comes from the database
                                        QtyToSchedule = component.QtyToSchedule * subQty, // User-defined editable value
                                        Run = nextRunNumber++.ToString()
                                    });

                                    processedPairs.Add((component.Component, subComponentId));
                                }
                            }
                        }
                    }
                }
            }

            // If no components are found, return just the master part
            if (!componentsFound)
            {
                allComponents.Add(new SintergyComponent
                {
                    MasterId = masterId,
                    Component = null,
                    SubComponent = null,
                    QtyToMakeMasterID = 1, // Default value
                    QtyToSchedule = quantity, // User-defined value
                    Run = nextRunNumber++.ToString()
                });
            }

            return allComponents;
        }



        private List<SintergyComponent> GetOpenParts()
        {
            var openParts = new List<SintergyComponent>();
            string query = "SELECT date, part, component, subcomponent, quantity, run, open FROM schedule WHERE open = 1";

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
                                Component = reader["component"]?.ToString(),
                                SubComponent = reader["subcomponent"]?.ToString(),
                                QtyToSchedule = Convert.ToInt32(reader["quantity"]),
                                Run = reader["run"].ToString(),
                                Open = Convert.ToInt32(reader["open"]) // Map the Open column
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
            string queryWithComponent = "INSERT INTO schedule (part, component, subcomponent, quantity, run, date, open) VALUES (@Part, @Component, @SubComponent, @Quantity, @Run, @Date, @Open)";
            string queryWithoutComponent = "INSERT INTO schedule (part, quantity, run, date, open) VALUES (@Part, @Quantity, @Run, @Date, @Open)";

            try
            {
                using (var connection = new MySqlConnection(connectionStringMySQL))
                {
                    connection.Open();
                    foreach (var component in viewModel.AllComponents)
                    {
                        // Determine if the component and subcomponent are null or empty
                        bool hasComponent = !string.IsNullOrWhiteSpace(component.Component);
                        bool hasSubComponent = !string.IsNullOrWhiteSpace(component.SubComponent);

                        using (var command = new MySqlCommand(hasComponent ? queryWithComponent : queryWithoutComponent, connection))
                        {
                            // Add shared parameters
                            command.Parameters.AddWithValue("@Part", component.MasterId);
                            command.Parameters.AddWithValue("@Quantity", component.QtyToSchedule);
                            command.Parameters.AddWithValue("@Run", component.Run);
                            command.Parameters.AddWithValue("@Date", DateTime.Now); // Set the current DateTime
                            command.Parameters.AddWithValue("@Open", 1); // Set open column to 1

                            // Add component and subcomponent parameters only if they exist
                            if (hasComponent)
                            {
                                command.Parameters.AddWithValue("@Component", component.Component);
                                command.Parameters.AddWithValue("@SubComponent", hasSubComponent ? component.SubComponent : (object)DBNull.Value);
                            }

                            command.ExecuteNonQuery();
                        }
                    }
                }

                TempData["Success"] = "Parts and components successfully scheduled!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"An error occurred while scheduling: {ex.Message}";
            }

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

        [HttpPost]
        public IActionResult UpdateOpenParts(ScheduleViewModel viewModel)
        {
            try
            {
                using (var connection = new MySqlConnection(connectionStringMySQL))
                {
                    connection.Open();
                    foreach (var part in viewModel.OpenParts)
                    {
                        string query = @"
                UPDATE schedule 
                SET date = @Date, part = @Part, component = @Component, subcomponent = @SubComponent, 
                    quantity = @Quantity, run = @Run, open = @Open 
                WHERE part = @Part AND run = @Run";

                        using (var command = new MySqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@Date", part.Date);
                            command.Parameters.AddWithValue("@Part", part.MasterId);
                            command.Parameters.AddWithValue("@Component", part.Component ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@SubComponent", part.SubComponent ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@Quantity", part.QtyToSchedule);
                            command.Parameters.AddWithValue("@Run", part.Run);
                            command.Parameters.AddWithValue("@Open", part.Open);

                            command.ExecuteNonQuery();
                        }
                    }
                }

                TempData["Success"] = "Open parts updated successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"An error occurred while updating open parts: {ex.Message}";
            }

            return RedirectToAction("Index");
        }

    }
}
