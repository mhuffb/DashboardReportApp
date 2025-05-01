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
        private readonly string _connectionStringDataflex;
        private readonly SharedService _sharedService;
        private readonly MoldingService _moldingService;

        public PressRunLogService(IConfiguration config, SharedService sharedService, MoldingService moldingService)
        {
            _connectionStringMySQL = config.GetConnectionString("MySQLConnection");
            _connectionStringDataflex = config.GetConnectionString("DataflexConnection");
            _sharedService = sharedService;
            _moldingService = moldingService;
        }

        #region Public CRUD Methods

        // ========== LOGIN ==========
        public async Task HandleLogin(PressRunLogModel formModel)
        {
            //insert main run record (skidnumber = 0)
            const string insertMainRun = @"
INSERT INTO pressrun 
    (operator, part, component, machine, prodNumber, run, startDateTime, skidNumber)
VALUES 
    (@operator, @part, @component, @machine, @prodNumber, @run, @startTime, 0)";

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            await using var cmd = new MySqlCommand(insertMainRun, conn);
            cmd.Parameters.AddWithValue("@operator", formModel.Operator);
            cmd.Parameters.AddWithValue("@part", formModel.Part);
            cmd.Parameters.AddWithValue("@component", formModel.Component);
            cmd.Parameters.AddWithValue("@machine", formModel.Machine);
            cmd.Parameters.AddWithValue("@prodNumber", formModel.ProdNumber);
            cmd.Parameters.AddWithValue("@run", formModel.Run);
            cmd.Parameters.AddWithValue("@startTime", formModel.StartDateTime);

            await cmd.ExecuteNonQueryAsync();


            //change operator on current skid
            await using var conn2 = new MySqlConnection(_connectionStringMySQL);
            await conn2.OpenAsync();

            // Get the current highest skidNumber for this run
            int currentSkid = 0;
            const string getSkidSql = @"
        SELECT IFNULL(MAX(skidNumber), 0)
        FROM pressrun
        WHERE run = @run AND skidNumber > 0";
            using (var cmd2 = new MySqlCommand(getSkidSql, conn2))
            {
                cmd2.Parameters.AddWithValue("@run", formModel.Run);
                var result = await cmd2.ExecuteScalarAsync();
                int.TryParse(result?.ToString(), out currentSkid);
            }

            if (currentSkid > 0)
            {
                const string closeSkidSql = @"
UPDATE pressrun
SET endDateTime = NOW(),
    pcsEnd = @pcsEnd,
    open = 1
WHERE run = @run
  AND skidNumber = @skidNum
  AND endDateTime IS NULL
LIMIT 1";

                using var closeCmd = new MySqlCommand(closeSkidSql, conn);
                closeCmd.Parameters.AddWithValue("@run", formModel.Run);
                closeCmd.Parameters.AddWithValue("@skidNum", currentSkid);
                closeCmd.Parameters.AddWithValue("@pcsEnd", formModel.PcsStart); // Close using new count
                await closeCmd.ExecuteNonQueryAsync();

                const string insertSql = @"
        INSERT INTO pressrun 
            (run, part, component, startDateTime, operator, machine, prodNumber, skidNumber, pcsStart)
        VALUES 
            (@run, @part, @component, @startDateTime, @operator, @machine, @prodNumber, @skidNumber, @pcsStart)";
                using (var insert = new MySqlCommand(insertSql, conn))
                {
                    insert.Parameters.AddWithValue("@run", formModel.Run);
                    insert.Parameters.AddWithValue("@part", formModel.Part);
                    insert.Parameters.AddWithValue("@component", formModel.Component);
                    insert.Parameters.AddWithValue("@startDateTime", formModel.StartDateTime);
                    insert.Parameters.AddWithValue("@operator", formModel.Operator);
                    insert.Parameters.AddWithValue("@machine", formModel.Machine);
                    insert.Parameters.AddWithValue("@prodNumber", formModel.ProdNumber);
                    insert.Parameters.AddWithValue("@skidNumber", currentSkid);
                    insert.Parameters.AddWithValue("@pcsStart", formModel.PcsStart);
                    await insert.ExecuteNonQueryAsync();
                }

            }

          
        }

        // ========== START SKID ==========
        public async Task HandleStartSkidAsync(PressRunLogModel model)
        {
            // Ends the previous skid if open, auto-prints a tag, then inserts a new skid, auto-prints a tag.
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // 1) Find the current highest skid number for this run
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

            // 2) If a skid is currently open (skidNumber == currentSkidNumber > 0 and endDateTime IS NULL),
            //    end it, fill pcsEnd, and mark open=1 so we know it's closed.
            if (currentSkidNumber > 0)
            {
                const string endSkidSql = @"
UPDATE pressrun
SET endDateTime = NOW(),
    pcsEnd = @pcsEnd,
    open = 1
WHERE run = @run
  AND skidNumber = @skidNum
  AND endDateTime IS NULL
LIMIT 1";
                using var endCmd = new MySqlCommand(endSkidSql, conn);
                endCmd.Parameters.AddWithValue("@run", model.Run);
                endCmd.Parameters.AddWithValue("@skidNum", currentSkidNumber);
                // We'll use the new 'start' count to close out the old skid's end count
                endCmd.Parameters.AddWithValue("@pcsEnd", model.PcsStart);
                int rowsUpdated = await endCmd.ExecuteNonQueryAsync();

                if (rowsUpdated > 0)
                {
                    // We ended the previous skid => auto-print a router tag for it.
                    // 3a) Get that ended skid record from DB
                    PressRunLogModel endedSkid = await GetPressRunRecordAsync(conn, model.Run, currentSkidNumber);
                    if (endedSkid != null)
                    {
                        // 3b) Generate PDF
                        string filePath = await GenerateRouterTagAsync(endedSkid);
                        // 3c) Print
                        _sharedService.PrintFileToClosestPrinter(filePath, 1);
                    }
                }
            }

            // 3) Insert the new skid
            int newSkidNumber = (currentSkidNumber == 0) ? 1 : currentSkidNumber + 1;
            const string insertNext = @"
INSERT INTO pressrun (run, part, component, startDateTime, operator, machine, prodNumber, skidNumber, pcsStart)
VALUES (@run, @part, @component, NOW(), @operator, @machine, @prodNumber, @skidNumber, @pcsStart);
SELECT LAST_INSERT_ID();";

            int newId = 0;
            using (var insSkid = new MySqlCommand(insertNext, conn))
            {
                insSkid.Parameters.AddWithValue("@run", model.Run);
                insSkid.Parameters.AddWithValue("@part", model.Part);
                insSkid.Parameters.AddWithValue("@component", model.Component);
                insSkid.Parameters.AddWithValue("@operator", model.Operator);
                insSkid.Parameters.AddWithValue("@machine", model.Machine);
                insSkid.Parameters.AddWithValue("@prodNumber", model.ProdNumber);
                insSkid.Parameters.AddWithValue("@skidNumber", newSkidNumber);
                insSkid.Parameters.AddWithValue("@pcsStart", model.PcsStart);

                object obj = await insSkid.ExecuteScalarAsync();
                if (obj != null && int.TryParse(obj.ToString(), out int insertedId))
                {
                    newId = insertedId;
                }
            }

            // 4) Auto-print router tag for the newly started skid
            if (newId > 0)
            {
                var newSkid = await GetPressRunRecordByIdAsync(conn, newId);
                if (newSkid != null)
                {
                    string filePath = await GenerateRouterTagAsync(newSkid);
                    _sharedService.PrintFileToClosestPrinter(filePath, 1);
                }
            }
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

        // ========== LOGOUT (No forced run end) ==========
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
        public async Task HandleEndRunAsync(int runId, int finalCount, int scrap, string notes, bool orderComplete)
        {
            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // 1) Find the run identifier from the main run (skidNumber=0)
            string mainRunIdentifier = "";
            const string selectMainRun = @"
SELECT run
FROM pressrun
WHERE id = @runId
  AND skidNumber = 0
ORDER BY id desc
LIMIT 1";
            using (var selectCmd = new MySqlCommand(selectMainRun, conn))
            {
                selectCmd.Parameters.AddWithValue("@runId", runId);
                object obj = await selectCmd.ExecuteScalarAsync();
                if (obj != null) mainRunIdentifier = obj.ToString();
            }

            if (!string.IsNullOrEmpty(mainRunIdentifier))
            {
                // 2) End any open skid record for this run
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
                    int skidRows = await endSkidCmd.ExecuteNonQueryAsync();

                    // If we ended one or more skids, print their tags
                    if (skidRows > 0)
                    {
                        // Grab the most recently ended skid for printing
                        string fetchEndedSkids = @"
SELECT id
FROM pressrun
WHERE run = @runIdentifier
  AND skidNumber > 0
  AND open = 1
  AND endDateTime IS NOT NULL
ORDER BY endDateTime DESC
LIMIT 1";

                        using (var fetchCmd = new MySqlCommand(fetchEndedSkids, conn))
                        {
                            fetchCmd.Parameters.AddWithValue("@runIdentifier", mainRunIdentifier);
                            var endedSkidIds = new List<int>();

                            using (var rdr = await fetchCmd.ExecuteReaderAsync())
                            {
                                if (await rdr.ReadAsync())
                                {
                                    endedSkidIds.Add(rdr.GetInt32(0));
                                }

                            }

                            // Generate & print each
                            foreach (int skidId in endedSkidIds)
                            {
                                PressRunLogModel endedSkid = await GetPressRunRecordByIdAsync(conn, skidId);
                                if (endedSkid != null)
                                {
                                    string filePath = await GenerateRouterTagAsync(endedSkid);
                                    _sharedService.PrintFileToClosestPrinter(filePath, 1);
                                }
                            }

                            // Mark them so we don't reprint later
                            string markPrinted = @"
UPDATE pressrun
SET open = 1
WHERE id IN (" + string.Join(",", endedSkidIds) + ")";
                            if (endedSkidIds.Count > 0)
                            {
                                using var markCmd = new MySqlCommand(markPrinted, conn);
                                await markCmd.ExecuteNonQueryAsync();
                            }
                        }
                    }
                }
            }

            // 3) End the main run
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

            // 4) Also mark presssetup open=0
            const string closeSetup = @"
    UPDATE presssetup
    SET open = 0
    WHERE run = (
        SELECT run 
        FROM pressrun 
        WHERE id = @runId 
          AND skidNumber = 0
    )";

            using (var closeCmd = new MySqlCommand(closeSetup, conn))
            {
                closeCmd.Parameters.AddWithValue("@runId", runId);
                int rowsAffected = await closeCmd.ExecuteNonQueryAsync();

                Console.WriteLine($"Rows updated in presssetup = {rowsAffected}");
            }



            // 5) If the user marked "Not Order Complete?", set schedule.open=1
            if (!orderComplete && !string.IsNullOrEmpty(mainRunIdentifier))
            {
                const string updateSchedule = @"
            UPDATE schedule
            SET open = 1
            WHERE run = @theRun
            LIMIT 1";
                using var schedCmd = new MySqlCommand(updateSchedule, conn);
                schedCmd.Parameters.AddWithValue("@theRun", mainRunIdentifier);
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
                document.SetMargins(40, 20, 60, 20);

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

        private PressRunLogModel ParseRunFromReader(DbDataReader rdr)
        {
            var model = new PressRunLogModel
            {
                Id = rdr.GetInt32("id"),
                Timestamp = rdr.GetDateTime("timestamp"),
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

        #endregion
    }
}
