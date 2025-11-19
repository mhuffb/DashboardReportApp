using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DashboardReportApp.Services
{
    public class ToolingWorkOrderService
    {
        private readonly string _connectionString;

        public ToolingWorkOrderService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection");
        }

        public List<ToolingWorkOrderModel> GetToolingWorkOrders()
        {
            var list = new List<ToolingWorkOrderModel>();
            const string sql = @"
SELECT Id, Part, PO, PoRequestedAt, Reason, ToolVendor, DateInitiated, DateDue,
       Cost, AccountingCode, InitiatedBy, DateReceived,
       Received_CompletedBy,
       AttachmentFileName        
FROM tooling_workorder_header
ORDER BY Id DESC;
";


            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ToolingWorkOrderModel
                {
                    Id = Convert.ToInt32(r["Id"]),
                    Part = r["Part"]?.ToString(),
                    PO = r["PO"]?.ToString(),
                    Reason = r["Reason"]?.ToString(),
                    ToolVendor = r["ToolVendor"]?.ToString(),
                    DateInitiated = Convert.ToDateTime(r["DateInitiated"]),
                    DateDue = r["DateDue"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateDue"]),
                    Cost = r["Cost"] == DBNull.Value ? null : (decimal?)Convert.ToDecimal(r["Cost"]),
                    AccountingCode = r["AccountingCode"] == DBNull.Value ? null : (int?)Convert.ToInt32(r["AccountingCode"]),
                    InitiatedBy = r["InitiatedBy"]?.ToString(),
                    DateReceived = r["DateReceived"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateReceived"]),
                    PoRequestedAt = r["PoRequestedAt"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["PoRequestedAt"]),
                    Received_CompletedBy = r["Received_CompletedBy"] == DBNull.Value ? null : r["Received_CompletedBy"]?.ToString(),
                    AttachmentFileName = r["AttachmentFileName"] == DBNull.Value ? null : r["AttachmentFileName"]?.ToString()


                });
            }
            return list;
        }


       


        public ToolingWorkOrderModel? GetToolingWorkOrdersById(int id)
        {
            const string sql = @"
SELECT Id, Part, PO, PoRequestedAt, Reason, ToolVendor, DateInitiated, DateDue,
       Cost, AccountingCode, InitiatedBy, DateReceived,
       Received_CompletedBy,
       AttachmentFileName        
FROM tooling_workorder_header
WHERE Id = @Id
LIMIT 1;
";


            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new ToolingWorkOrderModel
            {
                Id = Convert.ToInt32(r["Id"]),
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
                Received_CompletedBy = r["Received_CompletedBy"] == DBNull.Value ? null : r["Received_CompletedBy"]?.ToString(), 
                AttachmentFileName = r["AttachmentFileName"] == DBNull.Value ? null : r["AttachmentFileName"]?.ToString()

            };
        }
      
        public void MarkPoRequested(int id)
        {
            const string sql = @"
UPDATE tooling_workorder_header
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
INSERT INTO tooling_workorder_item
 (HeaderId, Action, ToolItem, ToolNumber, ToolDesc, Revision, Quantity, Cost, ToolWorkHours)
VALUES
 (@HeaderId, @Action, @ToolItem, @ToolNumber, @ToolDesc, @Revision, @Quantity, @Cost, @ToolWorkHours);";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@HeaderId", m.HeaderId);
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


        public void UpdateToolItem(ToolItemViewModel m)
        {
            const string sql = @"
UPDATE tooling_workorder_item SET
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
        public void AddToolingWorkOrder(ToolingWorkOrderModel m)
        {
            using var conn = new MySqlConnection(_connectionString);

            conn.Open();
            const string sql = @"
INSERT INTO tooling_workorder_header
 (Part, PO, Reason, ToolVendor, DateInitiated, DateDue, Cost,
   AccountingCode, InitiatedBy, DateReceived, Received_CompletedBy,
   AttachmentFileName)    
VALUES
 (@Part, @PO, @Reason, @ToolVendor, @DateInitiated, @DateDue, @Cost,
   @AccountingCode, @InitiatedBy, @DateReceived, @Received_CompletedBy,
   @AttachmentFileName);";

            using var cmd = new MySqlCommand(sql, conn);

          

            int ? accountingCode = m.Reason switch
            {
                "New" => 5030,
                "Repair" => 5045,
                "Breakage" => 5040,
                _ => null
            };

        
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
            cmd.Parameters.AddWithValue("@Received_CompletedBy", (object?)m.Received_CompletedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AttachmentFileName", (object?)m.AttachmentFileName ?? DBNull.Value);

            cmd.ExecuteNonQuery();



            m.Id = (int)cmd.LastInsertedId;
        }


        public void UpdateToolingWorkOrder(ToolingWorkOrderModel m)
        {
            const string sql = @"
UPDATE tooling_workorder_header SET
  Part=@Part, PO=@PO, Reason=@Reason, ToolVendor=@ToolVendor,
  DateInitiated=@DateInitiated, DateDue=@DateDue, Cost=@Cost, InitiatedBy=@InitiatedBy,
  DateReceived=@DateReceived,
  Received_CompletedBy=@Received_CompletedBy,
  AttachmentFileName=@AttachmentFileName         
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
            cmd.Parameters.AddWithValue("@Received_CompletedBy", (object?)m.Received_CompletedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AttachmentFileName", (object?)m.AttachmentFileName ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }





        public List<ToolItemViewModel> GetToolItemsByHeaderId(int headerId)
        {
            var list = new List<ToolItemViewModel>();
            const string sql = @"
SELECT i.Id, i.HeaderId, i.Action, i.ToolItem, i.ToolNumber, i.ToolDesc, i.Revision,
       i.Quantity, i.Cost, i.ToolWorkHours
FROM tooling_workorder_item i
WHERE i.HeaderId = @HeaderId
ORDER BY i.Id ASC;
";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@HeaderId", headerId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ToolItemViewModel
                {
                    Id = Convert.ToInt32(r["Id"]),
                    HeaderId = Convert.ToInt32(r["HeaderId"]),
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










        public ToolingWorkOrderModel? GetHeaderById(int id)
        {
            const string sql = @"
SELECT
    Id, Part, PO, Reason, ToolVendor, DateInitiated, DateDue,
    AccountingCode, Cost, InitiatedBy, DateReceived,
    Received_CompletedBy,
    AttachmentFileName            
FROM tooling_workorder_header
WHERE Id = @Id
LIMIT 1;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);



            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new ToolingWorkOrderModel
            {
                Id = Convert.ToInt32(r["Id"]),
                Part = r["Part"]?.ToString(),
                PO = r["PO"]?.ToString(),
                Reason = r["Reason"]?.ToString(),
                ToolVendor = r["ToolVendor"]?.ToString(),
                DateInitiated = r["DateInitiated"] != DBNull.Value ? Convert.ToDateTime(r["DateInitiated"]) : DateTime.Today,
                DateDue = r["DateDue"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(r["DateDue"]) : null,
                AccountingCode = r["AccountingCode"] != DBNull.Value ? (int?)Convert.ToInt32(r["AccountingCode"]) : null,
                Cost = r["Cost"] != DBNull.Value ? (decimal?)Convert.ToDecimal(r["Cost"]) : null,
                InitiatedBy = r["InitiatedBy"] != DBNull.Value ? r["InitiatedBy"]?.ToString() : null,
                DateReceived = r["DateReceived"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(r["DateReceived"]),        
                Received_CompletedBy = r["Received_CompletedBy"] == DBNull.Value ? null : r["Received_CompletedBy"]?.ToString(),
                AttachmentFileName = r["AttachmentFileName"] == DBNull.Value ? null : r["AttachmentFileName"]?.ToString()

            };
        }

        // === Packing Slip (QuestPDF) helpers ===

        public string SavePackingSlipPdf(int Id, string saveFolder)
        {
            if (string.IsNullOrWhiteSpace(saveFolder))
                throw new Exception("Save folder not provided.");

            var header = GetHeaderById(Id)
                         ?? throw new Exception($"No header found for Tool Work Order {Id}.");
            var items = GetToolItemsByHeaderId(Id) ?? new List<ToolItemViewModel>();

            Directory.CreateDirectory(saveFolder);
            var fileName = $"PackingSlip_G{header.Id}_{DateTime.Now:yyyyMMddHHmmss}.pdf";
            var fullPath = Path.Combine(saveFolder, SanitizeFileName(fileName));

            var bytes = BuildPackingSlipPdfBytes(header, items);
            File.WriteAllBytes(fullPath, bytes);
            return fullPath;
        }

        public byte[] BuildPackingSlipPdfBytes(ToolingWorkOrderModel header, List<ToolItemViewModel> items)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            // 🔹 Optional logo from wwwroot\img\SintergyLogo.bmp
            byte[]? logoBytes = null;
            try
            {
                var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var logoPath = Path.Combine(webRoot, "img", "SintergyLogo.bmp");  // your logo

                if (File.Exists(logoPath))
                    logoBytes = File.ReadAllBytes(logoPath);
            }
            catch
            {
                // ignore logo load failures – slip still renders
            }

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(36);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    // ===== HEADER =====
                    page.Header().Row(row =>
                    {
                        if (logoBytes != null)
                        {
                            // logo on the left
                            row.ConstantItem(80).Image(logoBytes);

                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text(t =>
                                {
                                    t.Span("SINTERGY, INC.")
                                     .FontSize(16)
                                     .SemiBold()
                                     .FontColor(Colors.Blue.Medium);
                                });

                                col.Item().Text(t =>
                                {
                                    t.Span("Tooling Packing Slip")
                                     .FontSize(12)
                                     .SemiBold();
                                });
                            });
                        }
                        else
                        {
                            // no logo – just text
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text(t =>
                                {
                                    t.Span("SINTERGY, INC.")
                                     .FontSize(16)
                                     .SemiBold()
                                     .FontColor(Colors.Blue.Medium);
                                });

                                col.Item().Text(t =>
                                {
                                    t.Span("Tooling Packing Slip")
                                     .FontSize(12)
                                     .SemiBold();
                                });
                            });

                            row.ConstantItem(80); // spacer to keep layout similar
                        }

                        // Right side: date + WO
                        row.ConstantItem(200).AlignRight().Column(col =>
                        {
                            col.Item().Text(t =>
                            {
                                t.Span($"Date: {DateTime.Now:MM-dd-yyyy}");
                            });

                            col.Item().Text(t =>
                            {
                                t.Span($"Tool Work Order: {header.Id}")
                                 .SemiBold();
                            });
                        });
                    });

                    // ===== BODY =====
                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        // -- Header info card --
                        col.Item()
                           .Border(1)
                           .BorderColor(Colors.Grey.Lighten1)
                           .Background(Colors.Grey.Lighten4)
                           .Padding(10)
                           .Column(info =>
                           {
                               info.Item().Text(t =>
                               {
                                   t.Span("Work Order Details")
                                    .SemiBold()
                                    .FontSize(12);
                               });

                               info.Item().PaddingTop(4).Table(t =>
                               {
                                   t.ColumnsDefinition(c =>
                                   {
                                       c.RelativeColumn();
                                       c.RelativeColumn();
                                   });

                                   void Line(string label, string value)
                                   {
                                       t.Cell().PaddingBottom(4).Column(c2 =>
                                       {
                                           c2.Item().Text(x =>
                                           {
                                               x.Span(label)
                                                .SemiBold()
                                                .FontColor(Colors.Grey.Darken2);
                                           });
                                           c2.Item().Text(value ?? string.Empty);
                                       });
                                   }

                                   Line("Assembly #", header.Part ?? "-");
                                   Line("Reason", header.Reason ?? "-");
                                   Line("Vendor", header.ToolVendor ?? "-");
                                   Line("PO #", header.PO ?? "-");
                                   Line("Initiated By", header.InitiatedBy ?? "-");
                                   Line("Date Initiated", header.DateInitiated.ToString("MM-dd-yyyy"));
                                   Line("Due Date", header.DateDue?.ToString("MM-dd-yyyy") ?? "-");
                                   Line("Estimated Cost", header.Cost?.ToString("C") ?? "-");
                               });
                           });

                        // -- Items title --
                        col.Item().PaddingTop(12).Text(t =>
                        {
                            t.Span("Items")
                             .SemiBold()
                             .FontSize(12);
                        });

                        // -- Items table --
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(24);      // #
                                c.RelativeColumn(1.2f);    // Action
                                c.RelativeColumn(1.6f);    // Item
                                c.RelativeColumn(1.1f);    // Tool #
                                c.RelativeColumn(2.5f);    // Desc
                                c.RelativeColumn(0.8f);    // Rev
                                c.ConstantColumn(40);      // Qty
                                c.ConstantColumn(60);      // Cost
                            });

                            // header row
                            t.Header(h =>
                            {
                                void HeaderCell(string text)
                                {
                                    h.Cell()
                                     .Element(e => e
                                        .Background(Colors.Grey.Lighten3)
                                        .BorderBottom(1)
                                        .BorderColor(Colors.Grey.Medium)
                                        .PaddingVertical(3)
                                        .PaddingHorizontal(2))
                                     .Text(x => x.Span(text).SemiBold());
                                }

                                HeaderCell("#");
                                HeaderCell("Action");
                                HeaderCell("Tool Item");
                                HeaderCell("Tool #");
                                HeaderCell("Description");
                                HeaderCell("Rev");
                                HeaderCell("Qty");
                                HeaderCell("Cost");
                            });

                            if (items != null && items.Any())
                            {
                                var index = 1;
                                var rowIndex = 0;

                                foreach (var it in items)
                                {
                                    bool isEven = rowIndex % 2 == 0;

                                    void Cell(string? text)
                                    {
                                        t.Cell()
                                         .Element(e =>
                                         {
                                             if (isEven)
                                                 e = e.Background(Colors.Grey.Lighten5);
                                             return e.PaddingVertical(2).PaddingHorizontal(2);
                                         })
                                         .Text(x => x.Span(text ?? string.Empty));
                                    }

                                    Cell(index.ToString());
                                    Cell(it.Action);
                                    Cell(it.ToolItem);
                                    Cell(it.ToolNumber);
                                    Cell(it.ToolDesc);
                                    Cell(it.Revision);
                                    Cell(it.Quantity == 0 ? "" : it.Quantity.ToString());
                                    Cell(it.Cost.HasValue ? it.Cost.Value.ToString("0.00") : "");

                                    index++;
                                    rowIndex++;
                                }
                            }
                            else
                            {
                                t.Cell().ColumnSpan(8)
                                    .PaddingVertical(6)
                                    .AlignCenter()
                                    .Text(x => x.Span("No items listed on this work order.")
                                                .Italic()
                                                .FontColor(Colors.Grey.Darken1));
                            }
                        });

                        // -- Signature area --
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

                    // ===== FOOTER =====
                    page.Footer().AlignCenter().Text(txt =>
                    {
                        txt.Span($"Packing Slip • Tool Work Order {header.Id}");

                        if (!string.IsNullOrWhiteSpace(header.Part))
                        {
                            txt.Span(" • ");
                            txt.Span(header.Part);
                        }
                    });
                });
            }).GeneratePdf();

            return pdfBytes;
        }


        private static string SanitizeFileName(string name)
        {
            foreach (var bad in Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');
            return name;
        }
        public List<string> GetDistinctReasons()
        {
            const string sql = @"SELECT DISTINCT Reason FROM tooling_workorder_header WHERE Reason IS NOT NULL AND Reason <> '' ORDER BY Reason;";
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
            const string sql = @"SELECT DISTINCT ToolVendor FROM tooling_workorder_header WHERE ToolVendor IS NOT NULL AND ToolVendor <> '' ORDER BY ToolVendor;";
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
            const string sql = @"SELECT DISTINCT InitiatedBy FROM tooling_workorder_header WHERE InitiatedBy IS NOT NULL AND InitiatedBy <> '' ORDER BY InitiatedBy;";
            var list = new List<string>();
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r["InitiatedBy"]?.ToString() ?? "");
            return list;
        }
        public void DeleteToolItem(int id)
        {
            const string sql = "DELETE FROM tooling_workorder_item WHERE Id=@Id;";
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }
        // Services/ToolingWorkOrderService.cs  (add these)
        public List<string> GetDistinctReceivers()
        {
            const string sql = @"
        SELECT DISTINCT Received_CompletedBy AS Name
        FROM tooling_workorder_header
        WHERE Received_CompletedBy IS NOT NULL AND Received_CompletedBy <> ''
        UNION
        SELECT DISTINCT InitiatedBy
        FROM tooling_workorder_header
        WHERE InitiatedBy IS NOT NULL AND InitiatedBy <> ''
        ORDER BY Name;";

            var list = new List<string>();
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetString(0));
            return list;
        }

        public void MarkWorkOrderComplete(int id, DateTime dateReceived, string receivedBy)
        {
            const string sql = @"
        UPDATE tooling_workorder_header
        SET DateReceived=@dateReceived,
            Received_CompletedBy=@receivedBy
        WHERE Id=@id;";

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@dateReceived", dateReceived);
            cmd.Parameters.AddWithValue("@receivedBy", receivedBy);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // Services/ToolingWorkOrderService.cs
        public List<string> GetDistinctToolItemNames()
        {
            const string sql = @"
        SELECT DISTINCT ToolItem FROM tooling_workorder_item
        WHERE ToolItem IS NOT NULL AND ToolItem <> ''
        UNION
        SELECT DISTINCT ToolItem FROM tooling_inventory
        WHERE ToolItem IS NOT NULL AND ToolItem <> ''
        ORDER BY ToolItem;";

            var list = new List<string>();
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetString(0));
            return list;
        }

    }
}
