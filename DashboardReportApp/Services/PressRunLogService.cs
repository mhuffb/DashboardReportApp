using DashboardReportApp.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Data.Common;
using System.Data;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Properties;
using iText.Kernel.Pdf.Canvas.Draw;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Geom;
using Microsoft.Extensions.Configuration;
using iText.Layout.Element;
using iText.Layout.Borders;

namespace DashboardReportApp.Services
{
    public class PressRunLogService
    {
        private readonly string _connectionStringMySQL;
        private readonly SharedService _sharedService;
        private readonly MoldingService _moldingService;

        public PressRunLogService(IConfiguration config, SharedService sharedService, MoldingService moldingService)
        {
            _connectionStringMySQL = config.GetConnectionString("MySQLConnection");
            _sharedService = sharedService;
            _moldingService = moldingService;
        }

        #region Public CRUD Methods

        // ========== LOGIN ==========
        /// <summary>Result returned by HandleLoginAsync so the UI knows what happened.</summary>
        public class LoginResult
        {
            public int SkidNumber { get; set; }   // 1‑based
            public bool NewSkid { get; set; }   // true = we created a new skid
            public string Message { get; set; }   // “Logged in and started skid 1” …
        }

       

        /// <summary>Returned by HandleStartSkidAsync so the UI can tell the user what happened.</summary>
        public class StartSkidResult
        {
            public int SkidNumber { get; set; }   // the skid that just started
            public string Message { get; set; }   // e.g. "Started skid 4."
        }

        // ========== START SKID ==========
        // ======================= START SKID (unchanged logic) ==================
        public async Task<StartSkidResult> HandleStartSkidAsync(PressRunLogModel model, int pcsStart)
        {
            var result = new StartSkidResult();

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // 1) Highest skid so far
            int currentSkidNumber = 0;
            const string getSkids = @"
        SELECT IFNULL(MAX(skidNumber),0)
        FROM pressrun
        WHERE run = @run AND skidNumber > 0;";
            using (var cmd = new MySqlCommand(getSkids, conn))
            {
                cmd.Parameters.AddWithValue("@run", model.Run);
                currentSkidNumber = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            // 2) Close the open skid (if any)
            if (currentSkidNumber > 0)
            {
                const string closeSql = @"
            UPDATE pressrun
            SET endDateTime = NOW(),
                pcsEnd      = @pcsEnd,
                open        = 1
            WHERE run = @run AND skidNumber = @skid AND endDateTime IS NULL
            LIMIT 1;";
                using var closeCmd = new MySqlCommand(closeSql, conn);
                closeCmd.Parameters.AddWithValue("@run", model.Run);
                closeCmd.Parameters.AddWithValue("@skid", currentSkidNumber);
                closeCmd.Parameters.AddWithValue("@pcsEnd", pcsStart);
                await closeCmd.ExecuteNonQueryAsync();
            }

            // 3) Insert the next skid
            int newSkidNumber = (currentSkidNumber == 0) ? 1 : currentSkidNumber + 1;
            const string insertSql = @"
        INSERT INTO pressrun
              (run, part, component, startDateTime, operator,
               machine, prodNumber, skidNumber, pcsStart)
        VALUES (@run, @part, @component, NOW(), @operator,
                @machine, @prod, @skid, @pcsStart);";
            using (var ins = new MySqlCommand(insertSql, conn))
            {
                ins.Parameters.AddWithValue("@run", model.Run);
                ins.Parameters.AddWithValue("@part", model.Part);
                ins.Parameters.AddWithValue("@component", model.Component);
                ins.Parameters.AddWithValue("@operator", model.Operator);
                ins.Parameters.AddWithValue("@machine", model.Machine);
                ins.Parameters.AddWithValue("@prod", model.ProdNumber);
                ins.Parameters.AddWithValue("@skid", newSkidNumber);
                ins.Parameters.AddWithValue("@pcsStart", pcsStart);
                await ins.ExecuteNonQueryAsync();
            }

            result.SkidNumber = newSkidNumber;
            result.Message = $"Started skid {newSkidNumber}.";
            return result;
        }

        // =======================  LOGIN  =======================
        public async Task<LoginResult> HandleLoginAsync(PressRunLogModel m)
        {
            var result = new LoginResult();

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // 🔐 Auto-logout any open main run on this machine
            int pcsEndForPrev = m.PcsStart ?? 0; // use the current device count you read in the modal
            var auto = await AutoLogoutIfMachineOccupiedAsync(conn, (MySqlTransaction)tx, m.Machine, pcsEndForPrev, m.Operator);

            // Always create the main run record (Skid 0)
            const string insertMain = @"
        INSERT INTO pressrun
              (operator, part, component, machine, prodNumber, run, startDateTime, skidNumber)
        VALUES (@operator, @part, @component, @machine, @prod, @run, @start, 0);";
            using (var cmd = new MySqlCommand(insertMain, conn, (MySqlTransaction)tx))
            {
                cmd.Parameters.AddWithValue("@operator", m.Operator);
                cmd.Parameters.AddWithValue("@part", m.Part);
                cmd.Parameters.AddWithValue("@component", m.Component);
                cmd.Parameters.AddWithValue("@machine", m.Machine);
                cmd.Parameters.AddWithValue("@prod", m.ProdNumber);
                cmd.Parameters.AddWithValue("@run", m.Run);
                cmd.Parameters.AddWithValue("@start", m.StartDateTime);
                await cmd.ExecuteNonQueryAsync();
            }

            // Look at all skids for this run (same logic as before, but inside the tx)
            const string checkSkids = @"
        SELECT skidNumber, open
        FROM pressrun
        WHERE run = @run AND skidNumber > 0
        ORDER BY skidNumber;";
            var openSkidNumber = 0;
            var allClosed = true;
            var maxSkid = 0;

            using (var cmd = new MySqlCommand(checkSkids, conn, (MySqlTransaction)tx))
            {
                cmd.Parameters.AddWithValue("@run", m.Run);
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    int skid = rdr.IsDBNull("skidNumber") ? 0 : rdr.GetInt32("skidNumber");
                    bool isOpen = !rdr.IsDBNull("open") && rdr.GetBoolean("open");
                    if (skid > maxSkid) maxSkid = skid;
                    if (isOpen) { openSkidNumber = skid; allClosed = false; }
                }
            }

            if (allClosed)
            {
                int newSkid = maxSkid + 1;
                result.SkidNumber = newSkid;
                result.NewSkid = true;
                result.Message = $"Logged in and started skid {newSkid}.";

                const string insertSkid = @"
            INSERT INTO pressrun
                  (run, part, component, startDateTime, operator,
                   machine, prodNumber, skidNumber, pcsStart)
            VALUES (@run, @part, @component, NOW(), @operator,
                    @machine, @prodNumber, @skid, @pcsStart);";
                using var newSkidCmd = new MySqlCommand(insertSkid, conn, (MySqlTransaction)tx);
                newSkidCmd.Parameters.AddWithValue("@run", m.Run);
                newSkidCmd.Parameters.AddWithValue("@part", m.Part);
                newSkidCmd.Parameters.AddWithValue("@component", m.Component);
                newSkidCmd.Parameters.AddWithValue("@operator", m.Operator);
                newSkidCmd.Parameters.AddWithValue("@machine", m.Machine);
                newSkidCmd.Parameters.AddWithValue("@prodNumber", m.ProdNumber);
                newSkidCmd.Parameters.AddWithValue("@skid", newSkid);
                newSkidCmd.Parameters.AddWithValue("@pcsStart", m.PcsStart);
                await newSkidCmd.ExecuteNonQueryAsync();
            }
            else
            {
                result.SkidNumber = openSkidNumber;
                result.NewSkid = false;
                result.Message = $"Logged in to existing skid {openSkidNumber}.";

                const string insertSkid = @"
            INSERT INTO pressrun
                  (run, part, component, startDateTime, operator,
                   machine, prodNumber, skidNumber, pcsStart)
            VALUES (@run, @part, @component, NOW(), @operator,
                    @machine, @prodNumber, @skid, @pcsStart);";
                using var insert = new MySqlCommand(insertSkid, conn, (MySqlTransaction)tx);
                insert.Parameters.AddWithValue("@run", m.Run);
                insert.Parameters.AddWithValue("@part", m.Part);
                insert.Parameters.AddWithValue("@component", m.Component);
                insert.Parameters.AddWithValue("@operator", m.Operator);
                insert.Parameters.AddWithValue("@machine", m.Machine);
                insert.Parameters.AddWithValue("@prodNumber", m.ProdNumber);
                insert.Parameters.AddWithValue("@skid", openSkidNumber);
                insert.Parameters.AddWithValue("@pcsStart", m.PcsStart);
                await insert.ExecuteNonQueryAsync();
            }

            // If we auto-logged someone out, append to the message
            if (auto.closed)
            {
                var who = string.IsNullOrWhiteSpace(auto.prevOperator) ? "previous operator" : auto.prevOperator;
                result.Message += $" (Auto-logged out {who} on machine {m.Machine})";
            }

            await tx.CommitAsync();

            // Print tag for the just-started/continued skid (same as your original)
            var latestRecord = await GetPressRunRecordAsync(conn, m.Run, result.SkidNumber);
            if (latestRecord != null)
            {
                string pdfFilePath = await GenerateRouterTagAsync(latestRecord);
                _sharedService.PrintFileToClosestPrinter(pdfFilePath, 1);
            }

            return result;
        }



        private static async Task InsertSkidRowAsync(MySqlConnection conn,
                                              PressRunLogModel m, int skidNo)
        {
            const string ins = @"
        INSERT INTO pressrun (run, part, component, startDateTime, operator,
                              machine, prodNumber, skidNumber, pcsStart, open)
        VALUES (@run,@part,@component,NOW(),@operator,
                @machine,@prod,@skid,@pcsStart,1);";   // open = 1 → current active line
            using var cmd = new MySqlCommand(ins, conn);
            cmd.Parameters.AddWithValue("@run", m.Run);
            cmd.Parameters.AddWithValue("@part", m.Part);
            cmd.Parameters.AddWithValue("@component", m.Component);
            cmd.Parameters.AddWithValue("@operator", m.Operator);
            cmd.Parameters.AddWithValue("@machine", m.Machine);
            cmd.Parameters.AddWithValue("@prod", m.ProdNumber);
            cmd.Parameters.AddWithValue("@skid", skidNo);
            cmd.Parameters.AddWithValue("@pcsStart", m.PcsStart ?? 0);
            await cmd.ExecuteNonQueryAsync();
        }


        public async Task<string> GetMachineForRunAsync(int runId)
        {
            const string sql = @"SELECT machine FROM pressrun WHERE id = @id LIMIT 1";
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", runId);

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "";
        }

        // ========== LOGOUT  ==========
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

            cmd.Parameters.AddWithValue("@scrap", scrap);
            cmd.Parameters.AddWithValue("@notes", notes ?? "");
            await cmd.ExecuteNonQueryAsync();


            //update the last open skid with the end count
            await using var conn2 = new MySqlConnection(_connectionStringMySQL);
            await conn2.OpenAsync();

            // Get the run number from main record (or just pass it if already known)
            string run = "";
            const string getRunSql = @"SELECT run FROM pressrun WHERE id = @id LIMIT 1";
            using (var cmd2 = new MySqlCommand(getRunSql, conn2))
            {
                cmd2.Parameters.AddWithValue("@id", runId);
                run = (await cmd2.ExecuteScalarAsync())?.ToString();
            }

            if (string.IsNullOrWhiteSpace(run)) return;

            // Update the last open skid (skidNumber > 0 and endDateTime is null)
            const string updateSkidSql = @"
        UPDATE pressrun
        SET pcsEnd = @pcsEnd,
            endDateTime = NOW(),
            scrap = @scrap,
            notes = @notes,
            open = 1
        WHERE run = @run
          AND skidNumber > 0
          AND endDateTime IS NULL
        ORDER BY skidNumber DESC
        LIMIT 1";
            using (var update = new MySqlCommand(updateSkidSql, conn2))
            {
                update.Parameters.AddWithValue("@pcsEnd", finalCount);
                update.Parameters.AddWithValue("@scrap", scrap);
                update.Parameters.AddWithValue("@notes", notes ?? "");
                update.Parameters.AddWithValue("@run", run);
                await update.ExecuteNonQueryAsync();
            }
        }

        // ========== END RUN (also ends any open skids) ==========
        public async Task HandleEndRunAsync(string run, int finalCount, int scrap, string notes, bool orderComplete)
        {
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // End any open skid
            const string endSkidQuery = @"
UPDATE pressrun
SET endDateTime = NOW(),
    pcsEnd = @finalCount,
    open = 1
WHERE run = @run
  AND skidNumber > 0
  AND endDateTime IS NULL";
            using (var endSkidCmd = new MySqlCommand(endSkidQuery, conn))
            {
                endSkidCmd.Parameters.AddWithValue("@run", run);
                endSkidCmd.Parameters.AddWithValue("@finalCount", finalCount);
                await endSkidCmd.ExecuteNonQueryAsync();
            }

            // End the main run
            const string endMainRun = @"
UPDATE pressrun
SET endDateTime = NOW(),
    scrap = @scrap,
    notes = @notes
WHERE run = @run
  AND skidNumber = 0
LIMIT 1";
            using (var endMainCmd = new MySqlCommand(endMainRun, conn))
            {
                endMainCmd.Parameters.AddWithValue("@run", run);
                endMainCmd.Parameters.AddWithValue("@scrap", scrap);
                endMainCmd.Parameters.AddWithValue("@notes", notes ?? "");
                await endMainCmd.ExecuteNonQueryAsync();
            }

            // Update presssetup
            const string closeSetup = @"UPDATE presssetup SET open = 0 WHERE run = @run";
            using (var closeCmd = new MySqlCommand(closeSetup, conn))
            {
                closeCmd.Parameters.AddWithValue("@run", run);
                await closeCmd.ExecuteNonQueryAsync();
            }

            // Reopen schedule if needed
            if (!orderComplete)
            {
                const string updateSchedule = @"UPDATE schedule SET open = 1 WHERE run = @run LIMIT 1";
                using var schedCmd = new MySqlCommand(updateSchedule, conn);
                schedCmd.Parameters.AddWithValue("@run", run);
                await schedCmd.ExecuteNonQueryAsync();
            }

        }

        #endregion

        #region Generate Tag PDF (with part factor & statistics)

        /// <summary>
        /// Generates a router tag PDF using iText, returns the file path,
        /// now including part-factor details and statistics for MOLD operations.
        /// </summary>
        public async Task<string> GenerateRouterTagAsync(PressRunLogModel model)
        {
            string part = string.IsNullOrWhiteSpace(model.Component) ? model.Part : model.Component;
            Console.WriteLine("Part: " + part);

            List<string> operations = _sharedService.GetOrderOfOps(part);
            string filePath = @"\\SINTERGYDC2024\Vol1\VSP\Exports\RouterTag_" + model.Id + ".pdf";

            PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            PdfFont normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            PdfWriter writer = new PdfWriter(filePath);
            PdfDocument pdf = new PdfDocument(writer);

            using (var document = new Document(pdf))
            {
                document.SetMargins(60, 20, 60, 20);

                // Title
                document.Add(new Paragraph("Sintergy Tracer Tag")
                    .SetFont(boldFont)
                    .SetFontSize(18)
                    .SetTextAlignment(TextAlignment.CENTER));

               
                document.Add(new LineSeparator(new SolidLine()).SetMarginBottom(10));

                string formattedStart = model.StartDateTime == default ? "" : model.StartDateTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                string formattedEnd = model.EndDateTime == null ? "" : model.EndDateTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                string id = model.Id.ToString();

                document.Add(new Paragraph("Order of Operations:")
                    .SetFont(boldFont)
                    .SetFontSize(14)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetMarginBottom(2));

                foreach (var op in operations)
                {
                    document.Add(new Paragraph(_sharedService.processDesc(op))
                        .SetFont(boldFont)
                        .SetFontSize(12)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetMarginBottom(2));

                    if (op.ToLower().Contains("machin"))
                    {
                        document.Add(new Paragraph("Machine #: ____________ Signature: __________________________   Date: __________________")
                            .SetFont(normalFont)
                            .SetFontSize(12)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetMarginBottom(5));
                    }
                    else if (op.ToLower().Contains("sinter"))
                    {
                        document.Add(new Paragraph("Loaded By: __________________   Unloaded By: __________________   Date: __________________")
                            .SetFont(normalFont)
                            .SetFontSize(12)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetMarginBottom(5));
                    }
                    else if (op.ToLower().Contains("mold"))
                    {
                        var headerTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1, 1 }))
                            .UseAllAvailableWidth()
                            .SetMarginBottom(10);

                        headerTable.AddCell(new Cell().Add(new Paragraph("Part: " + model.Part + " " + model.Component)
                            .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Press Run ID: " + id)
                            .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Skid Number: " + model.SkidNumber)
                            .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));

                        headerTable.AddCell(new Cell().Add(new Paragraph("Machine: " + model.Machine)
                            .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Prod Number: " + model.ProdNumber)
                            .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Starting Pcs: " + model.PcsStart)
                            .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));

                        headerTable.AddCell(new Cell().Add(new Paragraph("Operator: " + model.Operator)
                            .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Run: " + model.Run)
                            .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));
                        headerTable.AddCell(new Cell().Add(new Paragraph("Ending Pcs: " + ((model.PcsEnd ?? 0) == 0 ? "" : model.PcsEnd.ToString()))
                            .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));

                        document.Add(headerTable);

                        var timeTable = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1 }))
                            .UseAllAvailableWidth()
                            .SetMarginBottom(10);

                        timeTable.AddCell(new Cell().Add(new Paragraph($"Start DateTime: {model.StartDateTime}")
                            .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));
                        timeTable.AddCell(new Cell().Add(new Paragraph($"End DateTime: {model.EndDateTime}")
                            .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));

                        document.Add(timeTable);

                        string qcc_file_desc = await _sharedService.GetMostCurrentProlinkPart(part);
                        DateTime? endDt = model.EndDateTime ?? DateTime.Now;

                        DataTable partFactorDetails = await _sharedService.GetLatestPartFactorDetailsAsync(qcc_file_desc, null, null);

                        if (partFactorDetails != null && partFactorDetails.Rows.Count >= 4)
                        {
                            Table headerTable3 = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1, 1, 1 }))
                                .UseAllAvailableWidth()
                                .SetMarginBottom(10)
                                .SetBorder(Border.NO_BORDER);

                            for (int rowIndex = 2; rowIndex <= 3; rowIndex++)
                            {
                                DataRow row = partFactorDetails.Rows[rowIndex];
                                string mixLot = row[0].ToString();
                                string mixNumber = row[1].ToString();

                                headerTable3.AddCell(new Cell().Add(new Paragraph(mixLot)
                                    .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));
                                headerTable3.AddCell(new Cell().Add(new Paragraph(mixNumber)
                                    .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));
                            }
                            document.Add(headerTable3);
                        }
                        else
                        {
                            document.Add(new Paragraph("Not enough part-factor detail data available.")
                                .SetFont(normalFont).SetMarginBottom(10));
                        }

                        DataTable statistics = await _sharedService.GetStatisticsAsync(qcc_file_desc, model.StartDateTime);
                        document.Add(new Paragraph("Statistics:")
                            .SetFont(boldFont)
                            .SetFontSize(14)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetMarginTop(10)
                            .SetMarginBottom(10));

                        if (statistics != null && statistics.Rows.Count > 0)
                        {
                            int totalColumns = statistics.Columns.Count - 1;
                            if (totalColumns <= 0) totalColumns = 1;

                            Table statsTable = new Table(UnitValue.CreatePercentArray(totalColumns))
                                .UseAllAvailableWidth();

                            for (int i = 1; i < statistics.Columns.Count; i++)
                            {
                                statsTable.AddHeaderCell(new Cell().Add(new Paragraph(statistics.Columns[i].ColumnName)
                                    .SetFont(boldFont)));
                            }

                            foreach (DataRow row in statistics.Rows)
                            {
                                for (int i = 1; i < statistics.Columns.Count; i++)
                                {
                                    statsTable.AddCell(new Cell().Add(new Paragraph(row[i].ToString())
                                        .SetFont(normalFont)));
                                }
                            }

                            document.Add(statsTable);
                        }
                        else
                        {
                            document.Add(new Paragraph("No Statistics available.")
                                .SetFont(normalFont));
                        }
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

                float tagFontSize = 25f;            // make this as large as you like

                for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
                {
                    PdfPage page = pdf.GetPage(i);
                    Rectangle pageSize = page.GetPageSize();

                    // ---------- bottom footer ----------
                    Canvas footer = new Canvas(new PdfCanvas(page), pageSize)
                                        .SetFont(boldFont)          // ← bold
                                        .SetFontSize(tagFontSize);  // ← bigger
                    footer.ShowTextAligned(
                        part + "  Skid # " + model.SkidNumber,
                        pageSize.GetWidth() / 2,
                        pageSize.GetBottom() + 33,
                        TextAlignment.CENTER,
                        0f);                  // no rotation
                    footer.Close();

                    // ---------- upside-down header ----------
                    Canvas header = new Canvas(new PdfCanvas(page), pageSize)
                                        .SetFont(boldFont)
                                        .SetFontSize(tagFontSize);
                    header.ShowTextAligned(
                        part + "  Skid # " + model.SkidNumber,
                        pageSize.GetWidth() / 2,
                        pageSize.GetTop() - 20,
                        TextAlignment.CENTER,
                        (float)Math.PI);      // 180° rotation
                    header.Close();
                }


            }

            pdf.Close();
            return filePath;
        }


        #endregion

        #region Helpers


        private async Task<PressRunLogModel> GetPressRunRecordAsync(MySqlConnection conn, string run, int skidNumber)
        {
            const string sql = @"
SELECT id, timestamp, prodNumber, run, part, component, startDateTime, endDateTime,
       operator, machine, pcsStart, pcsEnd, scrap, notes, skidNumber
FROM pressrun
WHERE run = @run
  AND skidNumber = @skidNumber
ORDER BY id DESC
LIMIT 1";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@run", run);
            cmd.Parameters.AddWithValue("@skidNumber", skidNumber);

            using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                return ParseRunFromReader(rdr);
            }
            return null;
        }

        private async Task<PressRunLogModel> GetPressRunRecordByIdAsync(MySqlConnection conn, int id)
        {
            const string sql = @"
SELECT id, timestamp, prodNumber, run, part, component, startDateTime, endDateTime,
       operator, machine, pcsStart, pcsEnd, scrap, notes, skidNumber
FROM pressrun
WHERE id = @id
LIMIT 1";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                return ParseRunFromReader(rdr);
            }
            return null;
        }

        private static int TryOrdinal(DbDataReader rdr, string name)
        {
            try { return rdr.GetOrdinal(name); } catch { return -1; }
        }

        private PressRunLogModel ParseRunFromReader(DbDataReader rdr)
        {
            var tsOrd = TryOrdinal(rdr, "timestamp");
            var model = new PressRunLogModel
            {
                Id = rdr.GetInt32("id"),
                Timestamp = tsOrd >= 0 && !rdr.IsDBNull(tsOrd) ? rdr.GetDateTime(tsOrd) : DateTime.MinValue,
                ProdNumber = rdr.IsDBNull(rdr.GetOrdinal("prodNumber")) ? "" : rdr.GetString("prodNumber"),
                Run = rdr["run"]?.ToString(),
                Part = rdr["part"]?.ToString(),
                Component = rdr["component"]?.ToString(),
                StartDateTime = rdr.GetDateTime("startDateTime"),
                EndDateTime = rdr.IsDBNull(rdr.GetOrdinal("endDateTime")) ? null : rdr.GetDateTime("endDateTime"),
                Operator = rdr["operator"]?.ToString(),
                Machine = rdr["machine"]?.ToString(),
                PcsStart = rdr.IsDBNull(rdr.GetOrdinal("pcsStart")) ? (int?)null : rdr.GetInt32("pcsStart"),
                PcsEnd = rdr.IsDBNull(rdr.GetOrdinal("pcsEnd")) ? (int?)null : rdr.GetInt32("pcsEnd"),
                Scrap = rdr.IsDBNull(rdr.GetOrdinal("scrap")) ? (int?)null : rdr.GetInt32("scrap"),
                Notes = rdr["notes"]?.ToString(),
                SkidNumber = rdr.IsDBNull(rdr.GetOrdinal("skidNumber")) ? 0 : rdr.GetInt32("skidNumber")
            };
            return model;
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
            using var rdr = await cmd.ExecuteReaderAsync();
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
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var model = new PressSetupModel
                {
                    Id = rdr.GetInt32("id"),
                    Timestamp = rdr.GetDateTime("timestamp"),
                    Part = rdr.GetString("part"),
                    Component = rdr.IsDBNull(rdr.GetOrdinal("component")) ? "" : rdr.GetString("component"),
                    ProdNumber = rdr.IsDBNull(rdr.GetOrdinal("prodNumber")) ? "" : rdr.GetString("prodNumber"),
                    Run = rdr.GetString("run"),
                    Operator = rdr.GetString("operator"),
                    EndDateTime = rdr.IsDBNull(rdr.GetOrdinal("endDateTime"))
                        ? (DateTime?)null
                        : rdr.GetDateTime("endDateTime"),
                    Machine = rdr.GetString("machine"),
                    Notes = rdr.IsDBNull(rdr.GetOrdinal("notes")) ? "" : rdr.GetString("notes")
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
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(ParseRunFromReader(rdr));
            }
            return list;
        }

        public async Task<List<PressRunLogModel>> GetAllRunsAsync()
        {
            // In your original code, it calls the MoldingService to get them. 
            // If you want to see them from MySQL directly, you can do so here. 
            // For consistency, let's do exactly what your original code says:
            var list = _moldingService.GetPressRuns();
            return list;
        }
        public class PagedResult<T>
        {
            public IReadOnlyList<T> Rows { get; init; } = Array.Empty<T>();
            public long Total { get; init; }
            public int Page { get; init; }
            public int PageSize { get; init; }
        }

        public async Task<PagedResult<PressRunLogModel>> GetRunsPagedAsync(
            int page = 1,
            int pageSize = 100,
            string q = null,
            string machine = null,
            DateTime? start = null,
            DateTime? end = null)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 25, 1000);
            int offset = (page - 1) * pageSize;

            var rows = new List<PressRunLogModel>();
            long total;

            var where = new List<string>();
            var parms = new List<MySqlParameter>();

            if (!string.IsNullOrWhiteSpace(q))
            {
                where.Add("(part LIKE @q OR component LIKE @q OR prodNumber LIKE @q OR run LIKE @q OR operator LIKE @q)");
                parms.Add(new MySqlParameter("@q", $"%{q}%"));
            }
            if (!string.IsNullOrWhiteSpace(machine))
            {
                where.Add("machine = @machine");
                parms.Add(new MySqlParameter("@machine", machine));
            }
            if (start.HasValue) { where.Add("startDateTime >= @start"); parms.Add(new MySqlParameter("@start", start.Value)); }
            if (end.HasValue) { where.Add("startDateTime < @end"); parms.Add(new MySqlParameter("@end", end.Value)); }

            string whereSql = where.Count > 0 ? ("WHERE " + string.Join(" AND ", where)) : "";

            const string cols = @"id, timestamp, prodNumber, run, part, component, startDateTime, endDateTime,
                      operator, machine, pcsStart, pcsEnd, scrap, notes, skidNumber";

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // total count
            await using (var countCmd = new MySqlCommand($"SELECT COUNT(*) FROM pressrun {whereSql}", conn))
            {
                countCmd.Parameters.AddRange(parms.ToArray());
                total = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
            }

            // page of rows
            await using (var listCmd = new MySqlCommand($@"
        SELECT {cols}
        FROM pressrun
        {whereSql}
        ORDER BY startDateTime DESC
        LIMIT @limit OFFSET @offset;", conn))
            {
                listCmd.Parameters.AddRange(parms.ToArray());
                listCmd.Parameters.AddWithValue("@limit", pageSize);
                listCmd.Parameters.AddWithValue("@offset", offset);

                using var rdr = await listCmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    rows.Add(ParseRunFromReader(rdr));
            }

            return new PagedResult<PressRunLogModel>
            {
                Rows = rows,
                Total = total,
                Page = page,
                PageSize = pageSize
            };
        }
        public async Task<List<string>> GetMachinesAsync()
        {
            var list = new List<string>();
            const string sql = @"SELECT DISTINCT machine FROM pressrun ORDER BY machine";
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                list.Add(rdr.GetString(0));
            return list;
        }
        private async Task<(bool closed, string prevOperator, string prevRun)>
AutoLogoutIfMachineOccupiedAsync(MySqlConnection conn, MySqlTransaction tx,
                                 string machine, int pcsEndForPrev, string newOperator)
        {
            // Find an open main run (skid 0) on this machine
            const string findSql = @"
        SELECT id, run, operator
        FROM pressrun
        WHERE machine = @machine AND skidNumber = 0 AND endDateTime IS NULL
        ORDER BY id DESC
        LIMIT 1;";
            int prevMainId = 0;
            string prevRun = null;
            string prevOperator = null;

            using (var find = new MySqlCommand(findSql, conn, tx))
            {
                find.Parameters.AddWithValue("@machine", machine);
                using var rdr = await find.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    prevMainId = rdr.GetInt32("id");
                    prevRun = rdr["run"]?.ToString();
                    prevOperator = rdr["operator"]?.ToString();
                }
            }

            if (prevMainId == 0) return (false, null, null);

            // Close the main run
            const string closeMain = @"
        UPDATE pressrun
        SET endDateTime = NOW(),
            notes = CONCAT(IFNULL(notes,''), 
                   CASE WHEN IFNULL(notes,'') = '' THEN '' ELSE ' ' END,
                   '[Auto-logout by ', @newOp, ' ', DATE_FORMAT(NOW(),'%Y-%m-%d %H:%i'), ']')
        WHERE id = @id
        LIMIT 1;";
            using (var cmd = new MySqlCommand(closeMain, conn, tx))
            {
                cmd.Parameters.AddWithValue("@newOp", newOperator ?? "");
                cmd.Parameters.AddWithValue("@id", prevMainId);
                await cmd.ExecuteNonQueryAsync();
            }

            // Close the most-recent open skid for that run, if any
            const string closeSkid = @"
        UPDATE pressrun
        SET pcsEnd = @pcsEnd,
            endDateTime = NOW(),
            open = 1,
            notes = CONCAT(IFNULL(notes,''), 
                   CASE WHEN IFNULL(notes,'') = '' THEN '' ELSE ' ' END,
                   '[Auto-logout main closed]')
        WHERE run = @run
          AND skidNumber > 0
          AND endDateTime IS NULL
        ORDER BY skidNumber DESC
        LIMIT 1;";
            using (var cmd = new MySqlCommand(closeSkid, conn, tx))
            {
                cmd.Parameters.AddWithValue("@pcsEnd", pcsEndForPrev);
                cmd.Parameters.AddWithValue("@run", prevRun ?? "");
                await cmd.ExecuteNonQueryAsync();
            }

            return (true, prevOperator, prevRun);
        }

        #endregion
    }
}
