﻿using DashboardReportApp.Models;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Odbc;

namespace DashboardReportApp.Services
{
    public class ScheduleService
    {
        private readonly string _connectionStringMySQL;
        private readonly string _connectionStringDataflex;
        private readonly SharedService _sharedService;

        public ScheduleService(IConfiguration config, SharedService sharedService)
        {
            _connectionStringMySQL = config.GetConnectionString("MySQLConnection");
            _connectionStringDataflex = config.GetConnectionString("DataflexConnection");
            _sharedService = sharedService;
        }


        public List<SintergyComponent> GetComponentsForMasterId(string masterId, int quantity)
        {
            // 1. Load the entire BOM from masteras into a dictionary.
            //    Keys are parent part numbers; values are lists of (child, qty) pairs.
            var bom = new Dictionary<string, List<(string Child, int Qty)>>(StringComparer.OrdinalIgnoreCase);
            using (var connection = new OdbcConnection(_connectionStringDataflex))
            using (var command = new OdbcCommand("SELECT master_id, cmaster_id, qty FROM masteras", connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // Trim fields in case DataFlex uses fixed-length fields.
                        string parent = reader["master_id"].ToString().Trim();
                        string child = reader["cmaster_id"].ToString().Trim();
                        int qty = Convert.ToInt32(reader["qty"]);
                        if (!bom.ContainsKey(parent))
                            bom[parent] = new List<(string Child, int Qty)>();
                        bom[parent].Add((child, qty));
                    }
                }
            }

            // 2. Initialize run and prod numbers once.
            int nextRunNumber = GetNextRunNumber();
            int nextProdNumber = GetNextProdNumber();

            // 3. Use BFS to walk the BOM.
            //    The queue holds a tuple: (CurrentParent, CurrentPart, CumulativeMultiplier)
            //    For each edge (CurrentPart -> Child), we output a record with:
            //      Part = CurrentPart and Component = Child.
            var results = new List<SintergyComponent>();
            var queue = new Queue<(string Parent, string Current, int Multiplier)>();
            // Start with the top-level master.
            queue.Enqueue((masterId.Trim(), masterId.Trim(), quantity));

            while (queue.Count > 0)
            {
                var (parent, current, multiplier) = queue.Dequeue();

                if (bom.ContainsKey(current))
                {
                    foreach (var (child, qtyNeeded) in bom[current])
                    {
                        int newMultiplier = multiplier * qtyNeeded;
                        // Add a record for the edge: current -> child.
                        results.Add(new SintergyComponent
                        {
                            // Use the immediate parent's name as the part.
                            MasterId = current,
                            Component = child,
                            QtyNeededFor1Assy = qtyNeeded,
                            QtyToSchedule = newMultiplier,
                            Run = (nextRunNumber++).ToString(),
                            ProdNumber = (nextProdNumber).ToString()
                        });
                        // Enqueue the child so that its children are processed.
                        queue.Enqueue((child, child, newMultiplier));
                    }
                }
            }

            // 4. If the master has no children, return it as a leaf.
            if (results.Count == 0)
            {
                results.Add(new SintergyComponent
                {
                    MasterId = masterId,
                    Component = null,
                    QtyNeededFor1Assy = 1,
                    QtyToSchedule = quantity,
                    Run = (nextRunNumber++).ToString(),
                    ProdNumber = (nextProdNumber).ToString()
                });
            }

            return results;
        }





        public List<SintergyComponent> GetAllSchedParts()
        {
            var openParts = new List<SintergyComponent>();
            string query = "SELECT id, date, part, component, quantity, run, prodNumber, open FROM schedule ORDER BY id desc";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
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
                                Id = reader.GetInt32("id"),
                                Date = reader["date"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["date"]),
                                MasterId = reader["part"] == DBNull.Value ? string.Empty : reader["part"].ToString(),
                                Component = reader["component"] == DBNull.Value ? null : reader["component"].ToString(),
                                QtyToSchedule = reader["quantity"] == DBNull.Value ? 0 : Convert.ToInt32(reader["quantity"]),
                                Run = reader["run"] == DBNull.Value ? string.Empty : reader["run"].ToString(),
                                ProdNumber = reader["prodNumber"] == DBNull.Value ? string.Empty : reader["prodNumber"].ToString(),
                                Open = reader["open"] == DBNull.Value ? 0 : Convert.ToInt32(reader["open"])
                            };
                            openParts.Add(component);
                        }
                    }
                }
            }

            return openParts;
        }


        public bool CheckIfPartGetsSintergySecondary(string part)
        {
            List<string> result = _sharedService.GetOrderOfOps(part);

            bool honingFound = false;
            bool inHouseFound = false;
            bool machinFound = false;
            bool sintergyFound = false;
            bool tapFound = false;

            foreach (var op in result)
            {
                Console.WriteLine("Checking: " + op);
                if (op.IndexOf("Honing", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    honingFound = true;
                }
                if (op.IndexOf("In House", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    inHouseFound = true;
                }
                if (op.IndexOf("Machin", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    machinFound = true;
                }
                if (op.IndexOf("Sintergy", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sintergyFound = true;
                }
                if (op.IndexOf("Tap", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tapFound = true;
                }
            }

            bool condition1 = honingFound && inHouseFound;
            bool condition2 = machinFound && sintergyFound;
            bool condition3 = tapFound;

            Console.WriteLine("HoningFound: " + honingFound);
            Console.WriteLine("InHouseFound: " + inHouseFound);
            Console.WriteLine("MachinFound: " + machinFound);
            Console.WriteLine("SintergyFound: " + sintergyFound);
            Console.WriteLine("TapFound: " + tapFound);
            Console.WriteLine("Condition1 (Honing & In House): " + condition1);
            Console.WriteLine("Condition2 (Machin & Sintergy): " + condition2);
            Console.WriteLine("Condition3 (Tap): " + condition3);

            return condition1 || condition2 || condition3;
        }





        public void ScheduleComponents(ScheduleModel viewModel)
        {
            Console.WriteLine("Scheduling...");
            int nextProdNumber = GetNextProdNumber();
            
            string queryWithComponent = "INSERT INTO schedule (part, component, quantity, run, date, open, prodNumber, qtyNeededFor1Assy, getsSintergySecondary) VALUES (@Part, @Component, @Quantity, @Run, @Date, @Open, @ProdNumber, @QtyNeededFor1Assy, @GetsSintergySecondary)";
            string queryWithoutComponent = "INSERT INTO schedule (part, quantity, run, date, open, prodNumber, qtyNeededFor1Assy, getsSintergySecondary) VALUES (@Part, @Quantity, @Run, @Date, @Open, @ProdNumber, @QtyNeededFor1Assy, @GetsSintergySecondary)";

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                connection.Open();
                foreach (var component in viewModel.AllComponents)
                {
                    bool hasComponent = !string.IsNullOrWhiteSpace(component.Component);

                    using (var command = new MySqlCommand(hasComponent ? queryWithComponent : queryWithoutComponent, connection))
                    {
                        command.Parameters.AddWithValue("@Part", component.MasterId);
                        command.Parameters.AddWithValue("@Quantity", component.QtyToSchedule);
                        command.Parameters.AddWithValue("@Run", component.Run);
                        command.Parameters.AddWithValue("@Date", DateTime.Now);
                        command.Parameters.AddWithValue("@Open", 1);
                        command.Parameters.AddWithValue("@ProdNumber", nextProdNumber);
                        command.Parameters.AddWithValue("@QtyNeededFor1Assy", component.QtyNeededFor1Assy);

                        bool getsSintergySecondaryMasterID = CheckIfPartGetsSintergySecondary(component.MasterId);
                        bool getsSintergySecondaryComponent = CheckIfPartGetsSintergySecondary(component.MasterId);
                        if (getsSintergySecondaryMasterID || getsSintergySecondaryComponent)
                        {
                            command.Parameters.AddWithValue("@GetsSintergySecondary", 1);
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@GetsSintergySecondary", 0);
                        }

                        if (hasComponent)
                        {
                            command.Parameters.AddWithValue("@Component", component.Component);
                        }

                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        public int GetNextRunNumber()
        {
            int nextRunNumber = 0;

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                string query = "SELECT COALESCE(MAX(CAST(run AS UNSIGNED)), 0) + 1 AS NextRun FROM schedule";

                connection.Open();

                using (var command = new MySqlCommand(query, connection))
                {
                    var result = command.ExecuteScalar();
                    nextRunNumber = result != DBNull.Value ? Convert.ToInt32(result) : 1;
                }
            }

            return nextRunNumber;
        }
        public int GetNextProdNumber()
        {
            int nextProdNumber = 0;

            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                string query = "SELECT COALESCE(MAX(CAST(prodNumber AS UNSIGNED)), 0) + 1 AS NextProdNumber FROM schedule";
                connection.Open();

                using (var command = new MySqlCommand(query, connection))
                {
                    var result = command.ExecuteScalar();
                    nextProdNumber = result != DBNull.Value ? Convert.ToInt32(result) : 1;
                }
            }

            return nextProdNumber;
        }
        public void UpdatePart(SintergyComponent part)
        {
            using (var connection = new MySqlConnection(_connectionStringMySQL))
            {
                connection.Open();
                string query = @"
            UPDATE schedule 
            SET date = @Date, part = @Part, component = @Component, 
                quantity = @Quantity, run = @Run, open = @Open, prodNumber = @ProdNumber
            WHERE id = @Id";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Date", part.Date);
                    command.Parameters.AddWithValue("@Part", part.MasterId);
                    command.Parameters.AddWithValue("@Component", part.Component ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Quantity", part.QtyToSchedule);
                    command.Parameters.AddWithValue("@Run", part.Run);
                    command.Parameters.AddWithValue("@Open", part.Open);
                    command.Parameters.AddWithValue("@ProdNumber", part.ProdNumber);
                    command.Parameters.AddWithValue("@Id", part.Id); // Ensure this parameter is included
                    command.ExecuteNonQuery();
                }
            }
        }


    }
}
