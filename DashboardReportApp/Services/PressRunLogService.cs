using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.Data.Common;
using System.Data;
using iText.Layout.Element;
using System.Reflection.PortableExecutable;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout.Properties;
using iText.Layout;
using System.Collections;
using System.Data.Odbc;
using iText.Forms.Fields.Properties;
using iText.Layout.Borders;
using iText.Kernel.Pdf.Canvas.Draw;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Geom;

namespace DashboardReportApp.Services
{
    public class PressRunLogService
    {
        private readonly string _connectionStringMySQL;
        private readonly string _connectionStringDataflex;
        private readonly SharedService _sharedService;

        public PressRunLogService(IConfiguration config, SharedService sharedService)
        {
            _connectionStringMySQL = config.GetConnectionString("MySQLConnection");
            _connectionStringDataflex = config.GetConnectionString("DataflexConnection");
            _sharedService = sharedService;
        }

        #region Device Mapping / Count

        /// <summary>
        /// Maps a machine value to its device IP.
        /// If the machine string already contains a dot, it is assumed to be an IP.
        /// Otherwise, it is mapped using a dictionary.
        /// </summary>
        // Instead of your private MapMachineToIp method, you can do:
        private string MapMachineToIp(string machine)
        {
            return _sharedService.GetDeviceIp(machine);
        }

        /// <summary>
        /// Attempts to query the device’s count.
        /// Returns an integer if successful; otherwise, null.
        /// This method uses multiple parsing strategies.
        /// </summary>
        public async Task<int?> TryGetDeviceCountOrNull(string machine)
        {
            string deviceIp;
            try
            {
                deviceIp = MapMachineToIp(machine);
            }
            catch
            {
                return null;
            }

            try
            {
                // Configure a 5-second timeout (adjust as needed)
                using var httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(5)
                };

                string url = $"http://{deviceIp}/api/picodata";
                HttpResponseMessage response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string json = (await response.Content.ReadAsStringAsync()).Trim();
                Console.WriteLine("Device JSON: " + json);

                // Attempt to parse "count_value" from a JSON object
                if (json.StartsWith("{"))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    using var doc = JsonDocument.Parse(json);
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("count_value", out JsonElement countElement))
                    {
                        if (countElement.ValueKind == JsonValueKind.Number && countElement.TryGetInt32(out int deviceCount))
                        {
                            return deviceCount;
                        }
                        else if (countElement.ValueKind == JsonValueKind.String)
                        {
                            string countStr = countElement.GetString();
                            if (int.TryParse(countStr, out int parsedCount))
                            {
                                return parsedCount;
                            }
                        }
                    }
                }

                // Fallback: check if JSON is just a plain integer
                if (int.TryParse(json, out int plainCount))
                {
                    return plainCount;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in TryGetDeviceCountOrNull: " + ex.Message);
            }
            return null;
        }



        #endregion

        #region Main Run Logic (Login, Logout, EndRun)

        public async Task HandleLogin(PressRunLogModel formModel)
        {
            const string insertMainRun = @"
INSERT INTO pressrun 
    (operator, part, component,  machine, prodNumber, run, startDateTime, skidNumber)
VALUES 
    (@operator, @part, @component, @machine, @prodNumber, @run, @startTime, 0)";
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(insertMainRun, conn);
            cmd.Parameters.AddWithValue("@operator", formModel.Operator);
            cmd.Parameters.AddWithValue("@part", formModel.Part);
            cmd.Parameters.AddWithValue("@component", formModel.Component);
            // Store the raw machine value.
            cmd.Parameters.AddWithValue("@machine", formModel.Machine);
            cmd.Parameters.AddWithValue("@prodNumber", formModel.ProdNumber);
            cmd.Parameters.AddWithValue("@run", formModel.Run);
            cmd.Parameters.AddWithValue("@startTime", formModel.StartDateTime);
            await cmd.ExecuteNonQueryAsync();
        }

        // Updated Logout: now accepts scrap and notes.
        public async Task HandleLogoutAsync(int runId, int finalCount, int scrap, string notes)
        {
            const string sql = @"
UPDATE pressrun
SET endDateTime = NOW(),
    scrap = @scrap,
    notes = @notes
WHERE id = @runId
  AND skidNumber = 0
LIMIT 1";
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@runId", runId);
            cmd.Parameters.AddWithValue("@finalCount", finalCount);
            cmd.Parameters.AddWithValue("@scrap", scrap);
            cmd.Parameters.AddWithValue("@notes", notes ?? "");
            await cmd.ExecuteNonQueryAsync();
        }

        // Updated EndRun: ends any open skid record, then ends the main run.
        public async Task HandleEndRunAsync(int runId, int finalCount, int scrap, string notes)
        {
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // 1) Retrieve the run identifier from the main run record.
            string mainRunIdentifier = "";
            const string selectMainRun = @"
SELECT run FROM pressrun
WHERE id = @runId AND skidNumber = 0
LIMIT 1";
            using (var selectCmd = new MySqlCommand(selectMainRun, conn))
            {
                selectCmd.Parameters.AddWithValue("@runId", runId);
                object obj = await selectCmd.ExecuteScalarAsync();
                if (obj != null)
                    mainRunIdentifier = obj.ToString();
            }

            if (!string.IsNullOrEmpty(mainRunIdentifier))
            {
                // 2) End any open skid record for this run and update its pcsEnd.
                const string endSkidQuery = @"
UPDATE pressrun
SET endDateTime = NOW(),
    pcsEnd = @finalCount,
    open = 1
WHERE run = @runIdentifier
  AND skidNumber > 0
  AND endDateTime IS NULL";
                using (var endSkidCmd = new MySqlCommand(endSkidQuery, conn))
                {
                    endSkidCmd.Parameters.AddWithValue("@runIdentifier", mainRunIdentifier);
                    endSkidCmd.Parameters.AddWithValue("@finalCount", finalCount);
                    await endSkidCmd.ExecuteNonQueryAsync();
                }
            }

            // 3) End the main run record.
            const string endMainRun = @"
UPDATE pressrun
SET endDateTime = NOW(),
    scrap = @scrap,
    notes = @notes
WHERE id = @runId
  AND skidNumber = 0
LIMIT 1";
            using (var endMainCmd = new MySqlCommand(endMainRun, conn))
            {
                endMainCmd.Parameters.AddWithValue("@runId", runId);
                endMainCmd.Parameters.AddWithValue("@scrap", scrap);
                endMainCmd.Parameters.AddWithValue("@notes", notes ?? "");
                await endMainCmd.ExecuteNonQueryAsync();
            }

            // 4) Mark the run as closed in the presssetup table.
            const string closeSetup = @"
UPDATE presssetup
SET open = 0
WHERE run = (
    SELECT run FROM pressrun WHERE id = @runId AND skidNumber = 0
)
LIMIT 1";
            using (var closeCmd = new MySqlCommand(closeSetup, conn))
            {
                closeCmd.Parameters.AddWithValue("@runId", runId);
                await closeCmd.ExecuteNonQueryAsync();
            }

           
        }

        #endregion

        #region Skid Logic

        public async Task HandleStartSkidAsync(PressRunLogModel model)
        {
            Console.WriteLine("Handling Start Skid in Service...");
            Console.WriteLine($"Run: {model.Run}");
            Console.WriteLine($"Machine: {model.Machine}");
            Console.WriteLine($"Part: {model.Part}");
            Console.WriteLine($"Operator: {model.Operator}");
            Console.WriteLine($"Prod Number: {model.ProdNumber}");
            Console.WriteLine($"Initial Pcs Start: {model.PcsStart}");

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // 1) Count existing skid records for this run (skidNumber > 0)
            int currentSkidNumber = 0;
            const string getSkids = @"
    SELECT IFNULL(MAX(skidNumber), 0)
    FROM pressrun
    WHERE run = @run
      AND skidNumber > 0";
            using (var cmd = new MySqlCommand(getSkids, conn))
            {
                cmd.Parameters.AddWithValue("@run", model.Run);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && int.TryParse(result.ToString(), out int c))
                    currentSkidNumber = c;
            }

           
            // 3) Insert the first skid if no skids exist
            if (currentSkidNumber == 0)
            {
                const string insertFirst = @"
        INSERT INTO pressrun (run, part, component, startDateTime, operator, machine, prodNumber, skidNumber, pcsStart)
        VALUES (@run, @part, @component, NOW(), @operator, @machine, @prodNumber, 1, @pcsStart)";
                using var insCmd = new MySqlCommand(insertFirst, conn);
                insCmd.Parameters.AddWithValue("@run", model.Run);
                insCmd.Parameters.AddWithValue("@part", model.Part);
                insCmd.Parameters.AddWithValue("@component", model.Component);
                insCmd.Parameters.AddWithValue("@operator", model.Operator);
                insCmd.Parameters.AddWithValue("@machine", model.Machine);
                insCmd.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                insCmd.Parameters.AddWithValue("@pcsStart", model.PcsStart);  // Now using manual input
                await insCmd.ExecuteNonQueryAsync();
            }
            else
            {
                // 4) End previous skid and insert a new one
                int newSkidNumber = currentSkidNumber + 1;

                const string endSkidSql = @"
        UPDATE pressrun p
        JOIN (
            SELECT run, MAX(skidNumber) AS maxSkid
            FROM pressrun
            WHERE run = @run
              AND skidNumber > 0
              AND endDateTime IS NULL
            GROUP BY run
        ) t ON p.run = @run AND p.skidNumber = t.maxSkid
        SET p.endDateTime = NOW(),
            p.pcsEnd = @pcsEnd,
            p.open = 1
        WHERE p.endDateTime IS NULL";
                using var endCmd = new MySqlCommand(endSkidSql, conn);
                endCmd.Parameters.AddWithValue("@run", model.Run);
                endCmd.Parameters.AddWithValue("@pcsEnd", model.PcsStart); // End previous skid with same count
                await endCmd.ExecuteNonQueryAsync();

                // 5) Insert a new skid
                const string insertNext = @"
        INSERT INTO pressrun (run, part, component, startDateTime, operator, machine, prodNumber, skidNumber, pcsStart)
        VALUES (@run, @part, @component, NOW(), @operator, @machine, @prodNumber, @skidNumber, @pcsStart)";
                using var insSkid = new MySqlCommand(insertNext, conn);
                insSkid.Parameters.AddWithValue("@run", model.Run);
                insSkid.Parameters.AddWithValue("@part", model.Part);
                insSkid.Parameters.AddWithValue("@component", model.Component);
                insSkid.Parameters.AddWithValue("@operator", model.Operator);
                insSkid.Parameters.AddWithValue("@machine", model.Machine);
                insSkid.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                insSkid.Parameters.AddWithValue("@skidNumber", newSkidNumber);
                insSkid.Parameters.AddWithValue("@pcsStart", model.PcsStart);  // Now using manual input
                await insSkid.ExecuteNonQueryAsync();
            }

            Console.WriteLine("Start Skid Processed Successfully.");
        }

       
        public string GenerateRouterTag(PressRunLogModel model)
        {
            List<string> result = new List<string>();

            result = _sharedService.GetOrderOfOps(model.Part);


            string filePath = @"\\SINTERGYDC2024\Vol1\VSP\Exports\RouterTag_" + model.Id + ".pdf";

            // Use predefined fonts
            PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            PdfFont normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            // Create writer and pdf document
            PdfWriter writer = new PdfWriter(filePath);
            PdfDocument pdf = new PdfDocument(writer);
            using (var document = new Document(pdf))
            {
                // Set overall document margins (increased bottom margin for footer)
                document.SetMargins(20, 20, 40, 20);

                // Title at top center
                document.Add(new Paragraph("Sintergy Tracer Tag")
                    .SetFont(boldFont)
                    .SetFontSize(18)
                    .SetTextAlignment(TextAlignment.CENTER));

                // Prepare formatted StartDateTime
                string formattedStartDateTime = model.StartDateTime.ToString();
                string id = string.IsNullOrWhiteSpace(model.Id.ToString()) ? "N/A" : model.Id.ToString();

                



                // Add a horizontal line separator
                document.Add(new LineSeparator(new SolidLine()).SetMarginBottom(10));

                // Order of Operations header (centered)
                document.Add(new Paragraph("Order of Operations:")
                    .SetFont(boldFont)
                    .SetFontSize(14)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetMarginBottom(2));

                // Loop through each order operation item (keeping the same logic)
                foreach (var item in result)
                {
                    document.Add(new Paragraph(processDesc(item))
                        .SetFont(normalFont)
                        .SetFontSize(12)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetMarginBottom(2));

                    if (item.ToLower().Contains("machine"))
                    {
                        document.Add(new Paragraph("Machine #: ________________________   Date: __________________")
                            .SetFont(normalFont)
                            .SetFontSize(12)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetMarginBottom(5));
                    }
                    else if (item.ToLower().Contains("sinter"))
                    {
                        document.Add(new Paragraph("Loaded By: __________________   Unloaded By: __________________   Date: __________________")
                            .SetFont(normalFont)
                            .SetFontSize(12)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetMarginBottom(5));
                    }
                    else if (item.ToLower().Contains("mold"))
                    {
                        // Create a header table with 3 columns
                        Table headerTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1, 1 }))
                            .UseAllAvailableWidth()
                            .SetMarginBottom(10)
                            .SetBorder(Border.NO_BORDER);

                        
                        headerTable.AddCell(new Cell().Add(new Paragraph("Part: " + model.Part + " " + model.Component)
                            .SetFont(normalFont)
                            .SetFontSize(12))
                            .SetBorder(Border.NO_BORDER)
                            .SetTextAlignment(TextAlignment.LEFT));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Press Run ID: " + id)
                            .SetFont(normalFont)
                            .SetFontSize(12))
                            .SetBorder(Border.NO_BORDER)
                            .SetTextAlignment(TextAlignment.LEFT));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Skid Number: " + model.SkidNumber)
                            .SetFont(normalFont)
                            .SetFontSize(12))
                            .SetBorder(Border.NO_BORDER)
                            .SetTextAlignment(TextAlignment.LEFT));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Machine: " + model.Machine)
                            .SetFont(normalFont)
                            .SetFontSize(12))
                            .SetBorder(Border.NO_BORDER)
                            .SetTextAlignment(TextAlignment.LEFT));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Prod Number: " + model.ProdNumber)
                            .SetFont(normalFont)
                            .SetFontSize(12))
                            .SetBorder(Border.NO_BORDER)
                            .SetTextAlignment(TextAlignment.LEFT));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Starting Pcs: " + model.PcsStart)
                            .SetFont(normalFont)
                            .SetFontSize(12))
                            .SetBorder(Border.NO_BORDER)
                            .SetTextAlignment(TextAlignment.LEFT));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Operator: " + model.Operator)
                            .SetFont(normalFont)
                            .SetFontSize(12))
                            .SetBorder(Border.NO_BORDER)
                            .SetTextAlignment(TextAlignment.LEFT));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Run: " + model.Run)
                            .SetFont(normalFont)
                            .SetFontSize(12))
                            .SetBorder(Border.NO_BORDER)
                            .SetTextAlignment(TextAlignment.LEFT));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Ending Pcs: " + ((model.PcsEnd ?? 0) == 0 ? "" : model.PcsEnd.ToString()))
                            .SetFont(normalFont)
                            .SetFontSize(12))
                            .SetBorder(Border.NO_BORDER)
                            .SetTextAlignment(TextAlignment.LEFT));

                        document.Add(headerTable);

                        // Create a header table with 4 columns
                        Table headerTable2 = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 }))
                            .UseAllAvailableWidth()
                            .SetMarginBottom(10)
                            .SetBorder(Border.NO_BORDER);

                        headerTable2.AddCell(new Cell(1, 1).Add(new Paragraph("Start DateTime: " + formattedStartDateTime)
                            .SetFont(normalFont)
                            .SetFontSize(12))
                            .SetBorder(Border.NO_BORDER)
                            .SetTextAlignment(TextAlignment.LEFT));


                        string currentDate = DateTime.Now.ToString("MM/dd/yyyy hh:mm tt");
                        headerTable2.AddCell(new Cell(1, 1).Add(new Paragraph("End Date Time: " + currentDate)
                            .SetFont(normalFont)
                            .SetFontSize(12))
                            .SetBorder(Border.NO_BORDER)
                            .SetTextAlignment(TextAlignment.LEFT));
                        document.Add(headerTable2);
                    }
                    else
                    {
                        document.Add(new Paragraph("Signature: __________________________   Date: __________________")
                            .SetFont(normalFont)
                            .SetFontSize(12)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetMarginBottom(5));
                    }
                }

                // Now add the footer directly to the last page while the document is still open.
                PdfPage lastPage = pdf.GetLastPage();
                Rectangle pageSize = lastPage.GetPageSize();
                PdfCanvas pdfCanvas = new PdfCanvas(lastPage);
                Canvas footerCanvas = new Canvas(pdfCanvas, pageSize);
                footerCanvas.ShowTextAligned(
                    new Paragraph(model.Part)
                        .SetFont(boldFont)
                        .SetFontSize(25),
                    pageSize.GetWidth() / 2,
                    pageSize.GetBottom() + 33,  // 33 points above the bottom edge
                    TextAlignment.CENTER);
                footerCanvas.Close();
            }
            // Close the PDF document
            pdf.Close();

            return filePath; // Return the generated PDF file path
        }

        #endregion

        #region Retrieval

        public async Task<List<string>> GetOperatorsAsync()
        {
            var ops = new List<string>();
            const string sql = @"
SELECT name
FROM operators
WHERE dept = 'molding'
ORDER BY name";
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                ops.Add(rdr.GetString(0));
            }
            return ops;
        }

        public async Task<List<PressSetupModel>> GetOpenSetups()
        {
            var list = new List<PressSetupModel>();
            const string sql = @"
        SELECT id, timestamp, part, component, prodNumber, run, operator, endDateTime, machine, notes
        FROM presssetup
        WHERE open = 1
        ORDER BY part, run";

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var model = new PressSetupModel
                {
                    Id = rdr.GetInt32("id"),
                    Timestamp = rdr.GetDateTime("timestamp"),
                    Part = rdr.GetString("part"),
                    Component = rdr.IsDBNull(rdr.GetOrdinal("component"))
                                 ? ""
                                 : rdr.GetString("component"),                  

                    // Safely read prodNumber
                    ProdNumber = rdr.IsDBNull(rdr.GetOrdinal("prodNumber"))
                                 ? ""
                                 : rdr.GetString("prodNumber"),
                    Run = rdr.GetString("run"),
                    Operator = rdr.GetString("operator"),
                    EndDateTime = rdr.IsDBNull(rdr.GetOrdinal("endDateTime"))
                                 ? (DateTime?)null
                                 : rdr.GetDateTime("endDateTime"),
                    Machine = rdr.GetString("machine"),
                    Notes = rdr.IsDBNull(rdr.GetOrdinal("notes"))
                                 ? ""
                                 : rdr.GetString("notes")
                };

                list.Add(model);
            }
            return list;
        }



        public async Task<List<PressRunLogModel>> GetLoggedInRunsAsync()
        {
            var list = new List<PressRunLogModel>();
            const string sql = @"
SELECT id, timestamp, prodNumber, run, part, component, startDateTime, endDateTime,
       operator, machine, pcsStart, pcsEnd, scrap, notes, skidNumber
FROM pressrun
WHERE endDateTime IS NULL";

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(ParseRunFromReader(rdr));
            }
            return list;
        }

        public async Task<List<PressRunLogModel>> GetAllRunsAsync()
        {
            var list = new List<PressRunLogModel>();
            const string sql = @"
SELECT id, timestamp, prodNumber, run, part, component, startDateTime, endDateTime,
       operator, machine, pcsStart, pcsEnd, scrap, notes, skidNumber
FROM pressrun
ORDER BY id DESC";
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(ParseRunFromReader(rdr));
            }
            return list;
        }

        private PressRunLogModel ParseRunFromReader(DbDataReader rdr)
        {
            var model = new PressRunLogModel
            {
                Id = rdr.GetInt32("id"),
                Timestamp = rdr.GetDateTime("timestamp"),
                // Safely read prodNumber:
                ProdNumber = rdr.IsDBNull(rdr.GetOrdinal("prodNumber"))
                             ? ""
                             : rdr.GetString("prodNumber"),
                Run = rdr["run"]?.ToString(),
                Part = rdr["part"]?.ToString(),
                Component = rdr["component"]?.ToString(),
                StartDateTime = rdr.GetDateTime("startDateTime"),
                EndDateTime = rdr.IsDBNull(rdr.GetOrdinal("endDateTime"))
                                ? null
                                : rdr.GetDateTime("endDateTime"),
                Operator = rdr["operator"]?.ToString(),
                Machine = rdr["machine"]?.ToString(),
                PcsStart = rdr.IsDBNull(rdr.GetOrdinal("pcsStart"))
    ? (int?)null
    : rdr.GetInt32("pcsStart"),
                PcsEnd = rdr.IsDBNull(rdr.GetOrdinal("pcsEnd"))
    ? (int?)null
    : rdr.GetInt32("pcsEnd"),

                Scrap = rdr.IsDBNull(rdr.GetOrdinal("scrap"))
        ? (int?)null
        : rdr.GetInt32("scrap"),

                Notes = rdr["notes"]?.ToString(),
                SkidNumber = rdr.IsDBNull(rdr.GetOrdinal("skidNumber"))
                                ? 0
                                : rdr.GetInt32("skidNumber")
            };
            return model;
        }

        public string processDesc(string input)
        {
            switch (input)
            {
                case string a when a.Contains("HEAT TREAT-AHT") || a.Contains("HEAT TREAT - US HEAT") || a.Contains("HEAT TREAT - ELK COUNTY"): return "Heat Treat";

                case string st when st.Contains("Steam Treat") || st.Contains("STEAM TREAT"): return "Steam Treat";

                case string lsp when lsp.Contains("LS&PLATE") || lsp.Contains("LS & PLATE") || lsp.Contains("LS & Plate"): return "LS & Plate";

                case string Gr when Gr.Contains("Grind") || Gr.Contains("GRIND"): return "Grind";

                case string mc when mc.Contains("Machin"): return "Machine";

                case string et when et.Contains("Etching") || et.Contains("ETCHING"): return "Etching";

                case string hn when hn.Contains("Honing") || hn.Contains("HONING"): return "Honing";

                case string pl when pl.Contains("Plating") || pl.Contains("PLATING"): return "Plating";

                case string bo when bo.Contains("Black Oxide") || bo.Contains("BLACK OXIDE"): return "Black Oxide";

                case string mg when mg.Contains("Magni-Coat") || mg.Contains("MAGNI-COAT"): return "Magni-Coat";

                case string ls when ls.Contains("Loctite Seal") || ls.Contains("LOCTITE SEAL"): return "Loctite Seal";

                case string tb when tb.Contains("Tumbl") || tb.Contains("TUMBL"): return "Tumble";

            }
            return input;
        }
        #endregion


    }
}
