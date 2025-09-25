using DashboardReportApp.Models;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using System.IO; // if not already present for File I/O
namespace DashboardReportApp.Services
{
    public class ScheduleService
    {
        private readonly string _connectionStringMySQL;
        private readonly string _connectionStringSqlServersintTSQL;
        private readonly SharedService _sharedService;

        public ScheduleService(IConfiguration config, SharedService sharedService)
        {
            _connectionStringMySQL = config.GetConnectionString("MySQLConnection");
            _connectionStringSqlServersintTSQL = config.GetConnectionString("SqlServerConnectionsinTSQL");
            _sharedService = sharedService;
        }


        public List<SintergyComponent> GetComponentsForMasterId(string masterId, int quantity)
        {
            var bom = new Dictionary<string, List<(string Child, int Qty)>>(StringComparer.OrdinalIgnoreCase);

            // Pull from SQL Server table that replaced Dataflex "masteras"
            // Adjust the table/schema name if needed, e.g., dbo.masteras
            const string sql = @"SELECT master_id, cmaster_id, qty FROM dbo.masteras";

            using (var connection = new SqlConnection(_connectionStringSqlServersintTSQL))
            using (var command = new SqlCommand(sql, connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    int ordMaster = reader.GetOrdinal("master_id");
                    int ordCmaster = reader.GetOrdinal("cmaster_id");
                    int ordQty = reader.GetOrdinal("qty");

                    while (reader.Read())
                    {
                        // Handle potential trailing spaces from legacy imports
                        string parent = reader.IsDBNull(ordMaster) ? "" : reader.GetString(ordMaster).Trim();
                        string child = reader.IsDBNull(ordCmaster) ? "" : reader.GetString(ordCmaster).Trim();
                        int qty = reader.IsDBNull(ordQty) ? 0 : Convert.ToInt32(reader.GetValue(ordQty));

                        if (string.IsNullOrWhiteSpace(parent)) continue;
                        if (!bom.ContainsKey(parent))
                            bom[parent] = new List<(string Child, int Qty)>();
                        if (!string.IsNullOrWhiteSpace(child))
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
                    bool addedSLChild = false;

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

                        if (child.Contains("SL", StringComparison.OrdinalIgnoreCase))
                            addedSLChild = true;

                        queue.Enqueue((child, child, newMultiplier));
                    }

                    // Additional entry if parent doesn't contain 'Y' and has SL child
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
           string query = @"SELECT id, date, part, component, quantity, run, prodNumber, open, materialCode
                 FROM schedule ORDER BY id desc";


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
                                Open = reader["open"] == DBNull.Value ? 0 : Convert.ToInt32(reader["open"]),
                                MaterialCode = reader["materialCode"] == DBNull.Value ? null : reader["materialCode"].ToString(),

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
         openToSecondary, secondaryWorkFlag, materialCode)
        VALUES (@part, @comp, @qty, @run, @date, 1,
                @prod, @q1,
                @needsSec, @opsCnt,
                @openToSec, @flag, @mat)";

            const string sqlNoComponent = @"
        INSERT INTO schedule
        (part, quantity, run, date, open,
         prodNumber, qtyNeededFor1Assy,
         needsSintergySecondary, numberOfSintergySecondaryOps,
         openToSecondary, secondaryWorkFlag, materialCode)
        VALUES (@part, @qty, @run, @date, 1,
                @prod, @q1,
                @needsSec, @opsCnt,
                @openToSec, @flag, @mat)";

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
                cmd.Parameters.AddWithValue("@mat", c.MaterialCode ?? (object)DBNull.Value);

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
            SET date = @Date, 
                part = @Part, 
                component = @Component, 
                quantity = @Quantity, 
                run = @Run, 
                open = @Open, 
                prodNumber = @ProdNumber,
                materialCode = @MaterialCode      -- NEW
            WHERE id = @Id";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Date", (object?)part.Date ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Part", part.MasterId);
                    command.Parameters.AddWithValue("@Component", (object?)part.Component ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Quantity", part.QtyToSchedule);
                    command.Parameters.AddWithValue("@Run", part.Run);
                    command.Parameters.AddWithValue("@Open", part.Open);
                    command.Parameters.AddWithValue("@ProdNumber", part.ProdNumber);
                    command.Parameters.AddWithValue("@MaterialCode", (object?)part.MaterialCode ?? DBNull.Value); // NEW
                    command.Parameters.AddWithValue("@Id", part.Id);
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


        public void ReceivePowder(string originalFilename, byte[] fileBytes, int lot, decimal weight, string material)
        {
            // save PDF
            string dir = @"\\sintergydc2024\vol1\VSP\Uploads";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{originalFilename}");
            File.WriteAllBytes(path, fileBytes);

            // insert into DB
            const string sql = @"INSERT INTO powdermix (lotNumber, weightLbs, materialCode, createdAt)
                         VALUES (@lot, @wt, @mat, NOW());";

            using var conn = new MySqlConnection(_connectionStringMySQL);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@lot", lot);
            cmd.Parameters.AddWithValue("@wt", weight);
            cmd.Parameters.AddWithValue("@mat", material ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }


        private string ExtractTextFromPdf(byte[] pdfBytes)
        {
            var sb = new StringBuilder();

            using var reader = new PdfReader(new MemoryStream(pdfBytes));
            using var pdfDoc = new PdfDocument(reader);

            for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
            {
                var page = pdfDoc.GetPage(i);
                var strategy = new SimpleTextExtractionStrategy();
                var text = PdfTextExtractor.GetTextFromPage(page, strategy);
                sb.AppendLine(text);
            }

            return sb.ToString();
        }


        public (long lot, decimal weight, string material) ParsePowderPdf(byte[] fileBytes)
        {
            string content = ExtractTextFromPdf(fileBytes);

            // Normalize whitespace so regex is reliable
            content = Regex.Replace(content, @"\s+", " ");

            long lotNumber = 0;
            decimal weightVal = 0;
            string material = null;

            // ======== MATERIAL ========
            // Prefer "CUST CODE: XYZ" if present
            // ======== MATERIAL ========
            // Look for the first occurrence of "STY" and read up to the next space
            var styMatch = Regex.Match(content, @"(STY\S+)", RegexOptions.IgnoreCase);
            if (styMatch.Success)
            {
                material = styMatch.Groups[1].Value.Trim();
            }

            else
            {
                // fallback generic pattern e.g., STY-211 NAHvar matMatch = Regex.Match(content, @"\b([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+)\b");

                var matMatch = Regex.Match(content, @"\b([A-Z]{2,}\-\d{2,}(?:\s*[A-Z]+)?)\b");
                if (matMatch.Success)
                    material = matMatch.Groups[1].Value.Trim();
            }

            // ======== LOT NUMBER ========
            // 1) any explicit "Lot No" label
            var lotLabel = Regex.Match(content, @"Lot\s*(No|number)\s*[:\-]?\s*(\d{5,})", RegexOptions.IgnoreCase);
            if (lotLabel.Success)
            {
                long.TryParse(lotLabel.Groups[2].Value, out lotNumber);
            }

            // 2) fallback by looking for first all-digit token following material code
            if (lotNumber == 0 && material != null)
            {
                int idx = content.IndexOf(material);
                if (idx > 0)
                {
                    var tail = content.Substring(idx + material.Length);
                    var parts = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        var digitsOnly = Regex.Replace(p, @"\D", "");
                        if (digitsOnly.Length >= 5 && long.TryParse(digitsOnly, out long ln))
                        {
                            lotNumber = ln;
                            break;
                        }
                    }
                }
            }

            // ======== WEIGHT ========
            // 1) decimal lbs e.g. "19,980.29 Lbs"
            var w1 = Regex.Match(content, @"([\d,]+\.\d+)\s*Lbs?", RegexOptions.IgnoreCase);
            if (w1.Success)
            {
                decimal.TryParse(w1.Groups[1].Value.Replace(",", ""), out weightVal);
            }
            else
            {
                // 2) integer lbs e.g. "10000 lb"
                var w2 = Regex.Match(content, @"([\d,]+)\s*lb\b", RegexOptions.IgnoreCase);
                if (w2.Success)
                {
                    decimal.TryParse(w2.Groups[1].Value.Replace(",", ""), out weightVal);
                }
                else
                {
                    // 3) Quantity field (Advantage PDF): "QUANTITY: 518.00"
                    var q = Regex.Match(content, @"QUANTITY\s*[:\-]?\s*([\d,\.]+)", RegexOptions.IgnoreCase);
                    if (q.Success)
                        decimal.TryParse(q.Groups[1].Value.Replace(",", ""), out weightVal);
                }
            }

            return (lotNumber, weightVal, material);
        }


        public List<PowderMixEntry> GetPowderMixHistory()
        {
            var results = new List<PowderMixEntry>();
            const string sql = @"SELECT id, lotNumber, weightLbs, materialCode, createdAt
                         FROM powdermix
                         ORDER BY createdAt DESC";

            using var conn = new MySqlConnection(_connectionStringMySQL);
            conn.Open();

            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new PowderMixEntry
                {
                    Id = reader.GetInt32("id"), // NEW
                    LotNumber = reader.GetInt32("lotNumber"),
                    WeightLbs = reader.GetDecimal("weightLbs"),
                    MaterialCode = reader["materialCode"] == DBNull.Value ? null : reader["materialCode"].ToString(),
                    CreatedAt = reader.GetDateTime("createdAt")
                });
            }
            return results;
        }


        public void UpdatePowderMix(PowderMixEntry entry)
        {
            const string sql = @"UPDATE powdermix
                         SET lotNumber = @lot,
                             weightLbs = @wt,
                             materialCode = @mat
                         WHERE id = @id";

            using var conn = new MySqlConnection(_connectionStringMySQL);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@lot", entry.LotNumber);
            cmd.Parameters.AddWithValue("@wt", entry.WeightLbs);
            cmd.Parameters.AddWithValue("@mat", (object?)entry.MaterialCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", entry.Id);
            cmd.ExecuteNonQuery();
        }
        public void DeletePowderMix(int id)
        {
            const string sql = "DELETE FROM powdermix WHERE id = @id";
            using var conn = new MySqlConnection(_connectionStringMySQL);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

    }
}
