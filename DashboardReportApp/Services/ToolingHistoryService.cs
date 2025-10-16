using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DashboardReportApp.Services
{
    public class ToolingHistoryService
    {
        private readonly string _connectionString;

        public ToolingHistoryService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
        }

        public List<ToolingHistoryModel> GetToolingHistories()
        {
            var list = new List<ToolingHistoryModel>();
            const string sql = @"
SELECT Id, GroupID, Part, PO, PoRequestedAt, Reason, ToolVendor, DateInitiated, DateDue,
       Cost, AccountingCode, InitiatedBy, DateReceived
FROM tooling_history_header
ORDER BY Id DESC;
";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ToolingHistoryModel
                {
                    Id = Convert.ToInt32(r["Id"]),
                    GroupID = Convert.ToInt32(r["GroupID"]),
                    Part = r["Part"]?.ToString(),
                    PO = r["PO"]?.ToString(),
                    Reason = r["Reason"]?.ToString(),
                    ToolVendor = r["ToolVendor"]?.ToString(),
                    DateInitiated = Convert.ToDateTime(r["DateInitiated"]),
                    DateDue = r["DateDue"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateDue"]),
                    Cost = r["Cost"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(r["Cost"]),
                    AccountingCode = r["AccountingCode"] == DBNull.Value ? null : (int?)Convert.ToInt32(r["AccountingCode"]),
                    InitiatedBy = r["InitiatedBy"]?.ToString(),
                    DateReceived = r["DateReceived"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateReceived"]), // ADDED
                    PoRequestedAt = r["PoRequestedAt"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["PoRequestedAt"]),

                });
            }
            return list;
        }


        public int GetNextGroupID()
        {
            const string sql = "SELECT IFNULL(MAX(GroupID), 0) + 1 FROM tooling_history_header;";
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }


        public ToolingHistoryModel? GetToolingHistoryById(int id)
        {
            const string sql = @"
SELECT Id, GroupID, Part, PO, PoRequestedAt, Reason, ToolVendor, DateInitiated, DateDue,
       Cost, AccountingCode, InitiatedBy, DateReceived
FROM tooling_history_header
WHERE Id = @Id
LIMIT 1;

";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new ToolingHistoryModel
            {
                Id = Convert.ToInt32(r["Id"]),
                GroupID = Convert.ToInt32(r["GroupID"]),
                Part = r["Part"]?.ToString(),
                PO = r["PO"]?.ToString(),
                Reason = r["Reason"]?.ToString(),
                ToolVendor = r["ToolVendor"]?.ToString(),
                DateInitiated = Convert.ToDateTime(r["DateInitiated"]),
                DateDue = r["DateDue"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateDue"]),
                Cost = r["Cost"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(r["Cost"]),
                AccountingCode = r["AccountingCode"] == DBNull.Value ? null : (int?)Convert.ToInt32(r["AccountingCode"]),
                InitiatedBy = r["InitiatedBy"]?.ToString(),
                PoRequestedAt = r["PoRequestedAt"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["PoRequestedAt"]),
                DateReceived = r["DateReceived"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateReceived"]),

            };
        }
        private int GetHeaderIdByGroup(MySqlConnection conn, MySqlTransaction? tx, int groupId)
        {
            const string sql = "SELECT Id FROM tooling_history_header WHERE GroupID=@G LIMIT 1;";
            using var cmd = new MySqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@G", groupId);
            var o = cmd.ExecuteScalar();
            if (o == null || o == DBNull.Value) throw new Exception($"No header found for GroupID {groupId}.");
            return Convert.ToInt32(o);
        }
        public void MarkPoRequested(int id)
        {
            const string sql = @"
UPDATE tooling_history_header
SET PoRequestedAt = IFNULL(PoRequestedAt, NOW())
WHERE Id = @Id;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }


        public void AddToolItem(ToolItemViewModel m)
        {
            const string sql = @"
INSERT INTO tooling_history_item
 (HeaderId, GroupID, Action, ToolItem, ToolNumber, ToolDesc, Revision, Quantity, Cost, ToolWorkHours
  )
VALUES
 (@HeaderId, @GroupID, @Action, @ToolItem, @ToolNumber, @ToolDesc, @Revision, @Quantity, @Cost, @ToolWorkHours);";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();
            var headerId = GetHeaderIdByGroup(conn, tx, m.GroupID);

            using var cmd = new MySqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@HeaderId", headerId);
            cmd.Parameters.AddWithValue("@GroupID", m.GroupID);
            cmd.Parameters.AddWithValue("@Action", (object?)m.Action ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolItem", (object?)m.ToolItem ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolNumber", (object?)m.ToolNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolDesc", (object?)m.ToolDesc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Revision", (object?)m.Revision ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Quantity", (object?)m.Quantity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Cost", (object?)m.Cost ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolWorkHours", (object?)m.ToolWorkHours ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        public void UpdateToolItem(ToolItemViewModel m)
        {
            const string sql = @"
UPDATE tooling_history_item SET
  Action=@Action, ToolItem=@ToolItem, ToolNumber=@ToolNumber, ToolDesc=@ToolDesc, Revision=@Revision,
  Quantity=@Quantity, Cost=@Cost, ToolWorkHours=@ToolWorkHours
WHERE Id=@Id;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", m.Id);
            cmd.Parameters.AddWithValue("@Action", (object?)m.Action ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolItem", (object?)m.ToolItem ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolNumber", (object?)m.ToolNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolDesc", (object?)m.ToolDesc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Revision", (object?)m.Revision ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Quantity", (object?)m.Quantity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Cost", (object?)m.Cost ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolWorkHours", (object?)m.ToolWorkHours ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        public void AddToolingHistory(ToolingHistoryModel m)
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            // ensure unique GroupID
            using (var check = new MySqlCommand(
                "SELECT COUNT(*) FROM tooling_history_header WHERE GroupID=@G;", conn))
            {
                check.Parameters.AddWithValue("@G", m.GroupID);
                var exists = Convert.ToInt32(check.ExecuteScalar()) > 0;
                if (exists)
                {
                    using var maxCmd = new MySqlCommand("SELECT IFNULL(MAX(GroupID),0)+1 FROM tooling_history_header;", conn);
                    m.GroupID = Convert.ToInt32(maxCmd.ExecuteScalar());
                }
            }

            int? accountingCode = m.Reason switch
            {
                "New" => 5030,
                "Repair" => 5045,
                "Breakage" => 5040,
                _ => null
            };

            const string sql = @"
INSERT INTO tooling_history_header
 (GroupID, Part, PO, Reason, ToolVendor, DateInitiated, DateDue, Cost,
   AccountingCode, InitiatedBy, DateReceived)
VALUES
 (@GroupID, @Part, @PO, @Reason, @ToolVendor, @DateInitiated, @DateDue, @Cost,
   @AccountingCode, @InitiatedBy, @DateReceived);";



            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupID", m.GroupID);
            cmd.Parameters.AddWithValue("@Part", (object?)m.Part ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PO", (object?)m.PO ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Reason", (object?)m.Reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolVendor", (object?)m.ToolVendor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateInitiated", m.DateInitiated);
            cmd.Parameters.AddWithValue("@DateDue", (object?)m.DateDue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Cost", (object?)m.Cost ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AccountingCode", (object?)accountingCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@InitiatedBy", string.IsNullOrWhiteSpace(m.InitiatedBy) ? "Emery, J" : m.InitiatedBy);

            cmd.Parameters.AddWithValue("@DateReceived", (object?)m.DateReceived ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }


        public void UpdateToolingHistory(ToolingHistoryModel m)
        {
            const string sql = @"
UPDATE tooling_history_header SET
  Part=@Part, PO=@PO, Reason=@Reason, ToolVendor=@ToolVendor,
  DateInitiated=@DateInitiated, DateDue=@DateDue, Cost=@Cost, InitiatedBy=@InitiatedBy,
  DateReceived=@DateReceived
WHERE Id=@Id;";



            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", m.Id);
            cmd.Parameters.AddWithValue("@Part", (object?)m.Part ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PO", (object?)m.PO ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Reason", (object?)m.Reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolVendor", (object?)m.ToolVendor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateInitiated", m.DateInitiated);
            cmd.Parameters.AddWithValue("@DateDue", (object?)m.DateDue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Cost", (object?)m.Cost ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@InitiatedBy", string.IsNullOrWhiteSpace(m.InitiatedBy) ? (object)DBNull.Value : m.InitiatedBy);

            cmd.Parameters.AddWithValue("@DateReceived", (object?)m.DateReceived ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }





        public List<ToolItemViewModel> GetToolItemsByGroupID(int groupID)
        {
            var list = new List<ToolItemViewModel>();
            const string sql = @"
SELECT i.Id, i.GroupID, i.Action, i.ToolItem, i.ToolNumber, i.ToolDesc, i.Revision,
       i.Quantity, i.Cost, i.ToolWorkHours
FROM tooling_history_item i
WHERE i.GroupID = @GroupID
ORDER BY i.Id ASC;
";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupID", groupID);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ToolItemViewModel
                {
                    Id = Convert.ToInt32(r["Id"]),
                    GroupID = Convert.ToInt32(r["GroupID"]),
                    Action = r["Action"]?.ToString(),
                    ToolItem = r["ToolItem"]?.ToString(),
                    ToolNumber = r["ToolNumber"]?.ToString(),
                    ToolDesc = r["ToolDesc"]?.ToString(),
                    Revision = r["Revision"]?.ToString(),
                    Quantity = r["Quantity"] == DBNull.Value ? 0 : Convert.ToInt32(r["Quantity"]),

                    Cost = r["Cost"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(r["Cost"]),
                    ToolWorkHours = r["ToolWorkHours"] == DBNull.Value ? null : (int?)Convert.ToInt32(r["ToolWorkHours"])
                   
                });
            }
            return list;
        }



      
     

       
       
      

        public ToolingHistoryModel? GetHeaderByGroupID(int groupID)
        {
            const string sql = @"
SELECT
    Id, GroupID, Part, PO, Reason, ToolVendor, DateInitiated, DateDue,
    AccountingCode, Cost, InitiatedBy
FROM tooling_history_header
WHERE GroupID = @GroupID
LIMIT 1;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupID", groupID);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new ToolingHistoryModel
            {
                Id = Convert.ToInt32(r["Id"]),
                GroupID = Convert.ToInt32(r["GroupID"]),
                Part = r["Part"]?.ToString(),
                PO = r["PO"]?.ToString(),
                Reason = r["Reason"]?.ToString(),
                ToolVendor = r["ToolVendor"]?.ToString(),
                DateInitiated = r["DateInitiated"] != DBNull.Value ? Convert.ToDateTime(r["DateInitiated"]) : DateTime.Today,
                DateDue = r["DateDue"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(r["DateDue"]) : null,
                AccountingCode = r["AccountingCode"] != DBNull.Value ? (int?)Convert.ToInt32(r["AccountingCode"]) : null,
                Cost = r["Cost"] != DBNull.Value ? (decimal?)Convert.ToDecimal(r["Cost"]) : null,
                InitiatedBy = r["InitiatedBy"] != DBNull.Value ? r["InitiatedBy"]?.ToString() : null
            };
        }

        // === Packing Slip (QuestPDF) helpers ===

        public string SavePackingSlipPdf(int groupID, string saveFolder)
        {
            if (string.IsNullOrWhiteSpace(saveFolder))
                throw new Exception("Save folder not provided.");

            var header = GetHeaderByGroupID(groupID)
                         ?? throw new Exception($"No header found for GroupID {groupID}.");
            var items = GetToolItemsByGroupID(groupID) ?? new List<ToolItemViewModel>();

            Directory.CreateDirectory(saveFolder);
            var fileName = $"PackingSlip_G{header.GroupID}_{DateTime.Now:yyyyMMddHHmmss}.pdf";
            var fullPath = Path.Combine(saveFolder, SanitizeFileName(fileName));

            var bytes = BuildPackingSlipPdfBytes(header, items);
            File.WriteAllBytes(fullPath, bytes);
            return fullPath;
        }

        public byte[] BuildPackingSlipPdfBytes(ToolingHistoryModel header, List<ToolItemViewModel> items)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var docBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(36);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("SINTERGY, INC.").SemiBold().FontSize(16);
                            c.Item().Text("Tooling Packing Slip").FontSize(13);
                        });
                        r.ConstantItem(200).AlignRight().Column(c =>
                        {
                            c.Item().Text($"Date: {DateTime.Now:MM-dd-yyyy}");
                            c.Item().Text($"Group: {header.GroupID}");
                        });
                    });

                    page.Content().Column(col =>
                    {
                        col.Item().PaddingBottom(10).Table(t =>
                        {
                            // 3 equal columns, each showing a label on top and value underneath
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.RelativeColumn();
                                c.RelativeColumn();
                            });

                            void AddCell(string label, string value)
                            {
                                t.Cell().Column(c =>
                                {
                                    c.Item().Text(label).SemiBold();
                                    c.Item().Text(value);
                                });
                            }

                            AddCell("Part (Assembly #)", header.Part ?? "-");
                            AddCell("Reason", header.Reason ?? "-");
                            AddCell("Vendor", header.ToolVendor ?? "-");

                            AddCell("PO", header.PO ?? "-");
                            AddCell("Initiated By", header.InitiatedBy ?? "-");
                            AddCell("Date Initiated", header.DateInitiated.ToString("MM-dd-yyyy"));

                            AddCell("Due", header.DateDue?.ToString("MM-dd-yyyy") ?? "-");
                            AddCell("Est. Cost", header.Cost?.ToString("C") ?? "-");
                        });


                        col.Item().PaddingVertical(6).Text("Items").SemiBold().FontSize(12);

                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(28);
                                c.RelativeColumn(1.3f);
                                c.RelativeColumn(1.8f);
                                c.RelativeColumn(1.2f);
                                c.RelativeColumn(3);
                                c.RelativeColumn(0.8f);
                                c.ConstantColumn(42);
                            });

                            t.Header(h =>
                            {
                                h.Cell().Text("#").SemiBold();
                                h.Cell().Text("Action").SemiBold();
                                h.Cell().Text("Tool Item").SemiBold();
                                h.Cell().Text("Tool #").SemiBold();
                                h.Cell().Text("Description").SemiBold();
                                h.Cell().Text("Rev").SemiBold();
                                h.Cell().Text("Qty").SemiBold();
                                h.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium);
                            });

                            var i = 1;
                            foreach (var it in items)
                            {
                                t.Cell().Text((i++).ToString());
                                t.Cell().Text(it.Action ?? "");
                                t.Cell().Text(it.ToolItem ?? "");
                                t.Cell().Text(it.ToolNumber ?? "");
                                t.Cell().Text(it.ToolDesc ?? "");
                                t.Cell().Text(it.Revision ?? "");
                                t.Cell().Text(it.Quantity == 0 ? "" : it.Quantity.ToString());
                            }
                        });

                        col.Item().PaddingTop(16).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Packed By: _________________________");
                                c.Item().Text("Date: _____________________________");
                            });
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Received By: _______________________");
                                c.Item().Text("Date: _____________________________");
                            });
                        });
                    });

                    page.Footer().AlignCenter().Text($"Packing Slip • Group {header.GroupID} • {header.Part ?? "-"}");
                });
            }).GeneratePdf();

            return docBytes;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var bad in Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');
            return name;
        }
        public List<string> GetDistinctReasons()
        {
            const string sql = @"SELECT DISTINCT Reason FROM tooling_history_header WHERE Reason IS NOT NULL AND Reason <> '' ORDER BY Reason;";
            var list = new List<string>();
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r["Reason"]?.ToString() ?? "");
            return list;
        }

        public List<string> GetDistinctVendors()
        {
            const string sql = @"SELECT DISTINCT ToolVendor FROM tooling_history_header WHERE ToolVendor IS NOT NULL AND ToolVendor <> '' ORDER BY ToolVendor;";
            var list = new List<string>();
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r["ToolVendor"]?.ToString() ?? "");
            return list;
        }

        public List<string> GetDistinctInitiators()
        {
            const string sql = @"SELECT DISTINCT InitiatedBy FROM tooling_history_header WHERE InitiatedBy IS NOT NULL AND InitiatedBy <> '' ORDER BY InitiatedBy;";
            var list = new List<string>();
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r["InitiatedBy"]?.ToString() ?? "");
            return list;
        }

    }
}
