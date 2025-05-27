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
            var bom = new Dictionary<string, List<(string Child, int Qty)>>(StringComparer.OrdinalIgnoreCase);
            using (var connection = new OdbcConnection(_connectionStringDataflex))
            using (var command = new OdbcCommand("SELECT master_id, cmaster_id, qty FROM masteras", connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string parent = reader["master_id"].ToString().Trim();
                        string child = reader["cmaster_id"].ToString().Trim();
                        int qty = Convert.ToInt32(reader["qty"]);
                        if (!bom.ContainsKey(parent))
                            bom[parent] = new List<(string Child, int Qty)>();
                        bom[parent].Add((child, qty));
                    }
                }
            }

            int nextRunNumber = GetNextRunNumber();
            int nextProdNumber = GetNextProdNumber();

            var results = new List<SintergyComponent>();
            var queue = new Queue<(string Parent, string Current, int Multiplier)>();
            queue.Enqueue((masterId.Trim(), masterId.Trim(), quantity));

            while (queue.Count > 0)
            {
                var (parent, current, multiplier) = queue.Dequeue();

                if (bom.ContainsKey(current))
                {
                    bool addedSLChild = false; // ★ ADDED

                    foreach (var (child, qtyNeeded) in bom[current])
                    {
                        int newMultiplier = multiplier * qtyNeeded;

                        results.Add(new SintergyComponent
                        {
                            MasterId = current,
                            Component = child,
                            QtyNeededFor1Assy = qtyNeeded,
                            QtyToSchedule = newMultiplier,
                            Run = (nextRunNumber++).ToString(),
                            ProdNumber = nextProdNumber.ToString()
                        });

                        if (child.Contains("SL", StringComparison.OrdinalIgnoreCase)) // ★ ADDED
                            addedSLChild = true;

                        queue.Enqueue((child, child, newMultiplier));
                    }

                    // ★ ADDITIONAL ENTRY IF parent doesn't contain 'Y' and has SL child
                    if (!current.Contains("Y", StringComparison.OrdinalIgnoreCase) && addedSLChild)
                    {
                        results.Add(new SintergyComponent
                        {
                            MasterId = current,
                            Component = null,
                            QtyNeededFor1Assy = 1,
                            QtyToSchedule = multiplier,
                            Run = (nextRunNumber++).ToString(),
                            ProdNumber = nextProdNumber.ToString()
                        });
                    }
                }
            }

            if (results.Count == 0)
            {
                results.Add(new SintergyComponent
                {
                    MasterId = masterId,
                    Component = null,
                    QtyNeededFor1Assy = 1,
                    QtyToSchedule = quantity,
                    Run = (nextRunNumber++).ToString(),
                    ProdNumber = nextProdNumber.ToString()
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


        public int CheckIfPartNeedsSintergySecondary(string part)
        {
            List<string> result = _sharedService.GetOrderOfOps(part);
            int count = 0;

            foreach (var op in result)
            {
                Console.WriteLine("Checking: " + op);

                // Check for a combined condition in the current op
                bool hasSintergy = op.IndexOf("Sintergy", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasMachining = op.IndexOf("Machin", StringComparison.OrdinalIgnoreCase) >= 0;

                if (hasSintergy && hasMachining)
                {
                    count++;
                    Console.WriteLine("Combined condition met in operation: " + op);
                    continue; // Skip further checks if the op qualifies as combined
                }

                // Check for "Tap"
                if (op.IndexOf("Tap", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    count++;
                    Console.WriteLine("Condition met: 'Tap' found in: " + op);
                    continue;
                }

                // Check for "Honing"
                if (op.IndexOf("Honing", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    count++;
                    Console.WriteLine("Condition met: 'Honing' found in: " + op);
                }
            }

            Console.WriteLine("Debug: Total sintergy secondary ops count = " + count);
            return count;
        }








        public void ScheduleComponents(ScheduleModel viewModel)
        {
            Console.WriteLine("Scheduling…");
            int nextProdNumber = GetNextProdNumber();

            const string sqlWithComponent = @"
        INSERT INTO schedule
        (part, component, quantity, run, date, open,
         prodNumber, qtyNeededFor1Assy,
         needsSintergySecondary, numberOfSintergySecondaryOps,
         openToSecondary, secondaryWorkFlag)
        VALUES (@part, @comp, @qty, @run, @date, 1,
                @prod, @q1,
                @needsSec, @opsCnt,
                @openToSec, @flag)";

            const string sqlNoComponent = @"
        INSERT INTO schedule
        (part, quantity, run, date, open,
         prodNumber, qtyNeededFor1Assy,
         needsSintergySecondary, numberOfSintergySecondaryOps,
         openToSecondary, secondaryWorkFlag)
        VALUES (@part, @qty, @run, @date, 1,
                @prod, @q1,
                @needsSec, @opsCnt,
                @openToSec, @flag)";

            using var conn = new MySqlConnection(_connectionStringMySQL);
            conn.Open();

            foreach (var c in viewModel.AllComponents)
            {
                bool hasChild = !string.IsNullOrWhiteSpace(c.Component);
                string parentId = c.MasterId.Trim();
                string childId = hasChild ? c.Component.Trim() : null;

                // ── evaluate each ID independently ────────────────────────────────
                int parentOps = CheckIfPartNeedsSintergySecondary(parentId);
                int childOps = hasChild ? CheckIfPartNeedsSintergySecondary(childId) : 0;

                // flag logic
                int flag = 0;
                if (parentOps > 0 && childOps > 0) flag = 3;
                else if (childOps > 0) flag = 2;
                else if (parentOps > 0) flag = 1;

                int totalOps = flag switch           // how many ops we store
                {
                    1 => parentOps,
                    2 => childOps,
                    3 => parentOps + childOps,
                    _ => 0
                };

                bool needsSecondary = flag != 0;

                using var cmd = new MySqlCommand(hasChild ? sqlWithComponent : sqlNoComponent, conn);

                cmd.Parameters.AddWithValue("@part", parentId);
                if (hasChild) cmd.Parameters.AddWithValue("@comp", childId);

                cmd.Parameters.AddWithValue("@qty", c.QtyToSchedule);
                cmd.Parameters.AddWithValue("@run", c.Run);
                cmd.Parameters.AddWithValue("@date", DateTime.Now);
                cmd.Parameters.AddWithValue("@prod", nextProdNumber);
                cmd.Parameters.AddWithValue("@q1", c.QtyNeededFor1Assy);

                cmd.Parameters.AddWithValue("@needsSec", needsSecondary ? 1 : 0);
                cmd.Parameters.AddWithValue("@opsCnt", totalOps);
                cmd.Parameters.AddWithValue("@openToSec", needsSecondary ? 1 : 0);
                cmd.Parameters.AddWithValue("@flag", flag);

                cmd.ExecuteNonQuery();
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
        // put anywhere inside ScheduleService (private scope)
        private (int OpsCount, bool NeedsSecondary, bool UseComponent) GetSecondaryInfo(string parent, string child)
        {
            int partOps = CheckIfPartNeedsSintergySecondary(parent);
            int compOps = string.IsNullOrWhiteSpace(child) ? 0 : CheckIfPartNeedsSintergySecondary(child);

            // decide which number is actually going to secondary
            if (compOps > 0)              // component needs it
                return (compOps, true, true);

            if (partOps > 0)              // only the parent needs it
                return (partOps, true, false);

            return (0, false, false);     // neither needs it
        }



    }
}
