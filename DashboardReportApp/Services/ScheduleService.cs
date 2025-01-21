using DashboardReportApp.Models;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Odbc;

namespace DashboardReportApp.Services
{
    public class ScheduleService
    {
        private readonly IConfiguration _configuration;

        public ScheduleService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string MySQLConnectionString => _configuration.GetConnectionString("MySQLConnection");
        public string DataflexConnectionString => _configuration.GetConnectionString("DataflexConnection");

        public List<SintergyComponent> GetComponentsForMasterId(string masterId, int quantity)
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

                using (var connection = new OdbcConnection(DataflexConnectionString))
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
                            componentsFound = true;
                            string componentId = reader["cmaster_id"].ToString();
                            int qty = Convert.ToInt32(reader["qty"]);

                            if (!processedPairs.Contains((currentMasterId, componentId)))
                            {
                                allComponents.Add(new SintergyComponent
                                {
                                    MasterId = masterId,
                                    Component = componentId,
                                    SubComponent = null,
                                    QtyToMakeMasterID = qty,
                                    QtyToSchedule = quantity * qty,
                                    Run = nextRunNumber++.ToString()
                                });

                                processedPairs.Add((currentMasterId, componentId));
                                queue.Enqueue(componentId);
                            }
                        }
                    }

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
                                        QtyToMakeMasterID = subQty,
                                        QtyToSchedule = component.QtyToSchedule * subQty,
                                        Run = nextRunNumber++.ToString()
                                    });

                                    processedPairs.Add((component.Component, subComponentId));
                                }
                            }
                        }
                    }
                }
            }

            if (!componentsFound)
            {
                allComponents.Add(new SintergyComponent
                {
                    MasterId = masterId,
                    Component = null,
                    SubComponent = null,
                    QtyToMakeMasterID = 1,
                    QtyToSchedule = quantity,
                    Run = nextRunNumber++.ToString()
                });
            }

            return allComponents;
        }

        public List<SintergyComponent> GetOpenParts()
        {
            var openParts = new List<SintergyComponent>();
            string query = "SELECT date, part, component, subcomponent, quantity, run, open FROM schedule WHERE open = 1";

            using (var connection = new MySqlConnection(MySQLConnectionString))
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
                                Date = reader["date"] as DateTime?,
                                MasterId = reader["part"].ToString(),
                                Component = reader["component"]?.ToString(),
                                SubComponent = reader["subcomponent"]?.ToString(),
                                QtyToSchedule = Convert.ToInt32(reader["quantity"]),
                                Run = reader["run"].ToString(),
                                Open = Convert.ToInt32(reader["open"])
                            };
                            openParts.Add(component);
                        }
                    }
                }
            }

            return openParts;
        }

        public void ScheduleComponents(ScheduleViewModel viewModel)
        {
            string queryWithComponent = "INSERT INTO schedule (part, component, subcomponent, quantity, run, date, open) VALUES (@Part, @Component, @SubComponent, @Quantity, @Run, @Date, @Open)";
            string queryWithoutComponent = "INSERT INTO schedule (part, quantity, run, date, open) VALUES (@Part, @Quantity, @Run, @Date, @Open)";

            using (var connection = new MySqlConnection(MySQLConnectionString))
            {
                connection.Open();
                foreach (var component in viewModel.AllComponents)
                {
                    bool hasComponent = !string.IsNullOrWhiteSpace(component.Component);
                    bool hasSubComponent = !string.IsNullOrWhiteSpace(component.SubComponent);

                    using (var command = new MySqlCommand(hasComponent ? queryWithComponent : queryWithoutComponent, connection))
                    {
                        command.Parameters.AddWithValue("@Part", component.MasterId);
                        command.Parameters.AddWithValue("@Quantity", component.QtyToSchedule);
                        command.Parameters.AddWithValue("@Run", component.Run);
                        command.Parameters.AddWithValue("@Date", DateTime.Now);
                        command.Parameters.AddWithValue("@Open", 1);

                        if (hasComponent)
                        {
                            command.Parameters.AddWithValue("@Component", component.Component);
                            command.Parameters.AddWithValue("@SubComponent", hasSubComponent ? component.SubComponent : (object)DBNull.Value);
                        }

                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        public int GetNextRunNumber()
        {
            int nextRunNumber = 0;

            using (var connection = new MySqlConnection(MySQLConnectionString))
            {
                string query = "SELECT MAX(run) + 1 AS NextRun FROM schedule";
                connection.Open();

                using (var command = new MySqlCommand(query, connection))
                {
                    var result = command.ExecuteScalar();
                    nextRunNumber = result != DBNull.Value ? Convert.ToInt32(result) : 1;
                }
            }

            return nextRunNumber;
        }

        public void UpdateOpenParts(ScheduleViewModel viewModel)
        {
            try
            {
                using (var connection = new MySqlConnection(MySQLConnectionString))
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
            }
            catch (Exception ex)
            {
                throw new Exception($"An error occurred while updating open parts: {ex.Message}");
            }
        }
    }
}
