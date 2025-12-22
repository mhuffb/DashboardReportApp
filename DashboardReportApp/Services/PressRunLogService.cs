using DashboardReportApp.Models;
using iText.Barcodes;
using iText.IO.Font.Constants;
using iText.Kernel.Colors; // only needed if you use the overload with colors
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Draw;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace DashboardReportApp.Services
{
    public class PressRunLogService
    {
        private readonly string _connectionStringMySQL;
        private readonly SharedService _sharedService;
        private readonly MoldingService _moldingService;
        private readonly string _exportsFolder;

        public PressRunLogService(IConfiguration config, SharedService sharedService, MoldingService moldingService)
        {
            _connectionStringMySQL = config.GetConnectionString("MySQLConnection");
            _sharedService = sharedService;
            _moldingService = moldingService;

            _exportsFolder = config["Paths:PressRunExports"]
                             ?? @"\\SINTERGYDC2024\Vol1\VSP\Exports";
        }


        #region Public CRUD Methods

        public class LoginResult
        {
            public int SkidNumber { get; set; }      // 1-based
            public bool NewSkid { get; set; }        // true = created a new skid
            public string Message { get; set; } = "";

            // 🔹 Override-related info
            public bool RequiresOverride { get; set; }      // true => supervisor PIN needed
            public bool OverrideUsed { get; set; }          // true => override applied (new or prior)
            public string? Supervisor { get; set; }         // name from supervisors table
            public string Code { get; set; } = "OK";        // "OK", "MATERIAL_MISMATCH", "BAD_PIN"
            public string? ScheduledMaterial { get; set; }  // scheduled materialCode
            public string? ScannedMaterial { get; set; }    // current mix materialCode
        }

        public class StartSkidResult
        {
            public int SkidNumber { get; set; }      // skid that just started
            public string Message { get; set; } = "";

            // 🔹 Override-related info
            public bool RequiresOverride { get; set; }
            public bool OverrideUsed { get; set; }
            public string? Supervisor { get; set; }
            public string Code { get; set; } = "OK";        // "OK", "MATERIAL_MISMATCH", "BAD_PIN"
            public string? ScheduledMaterial { get; set; }
            public string? ScannedMaterial { get; set; }
        }


        public async Task<(string LotNumber, string MaterialCode)?> GetCurrentMixForMachineAsync(string machine)
        {
            if (string.IsNullOrWhiteSpace(machine))
                return null;

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            const string sql = @"
    SELECT LotNumber, MaterialCode
    FROM pressmixbagchange
    WHERE Machine = @machine
    ORDER BY id DESC  
    LIMIT 1;
";


            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@machine", machine);

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);

            if (!await reader.ReadAsync())
                return null;

            var lot = reader["LotNumber"]?.ToString() ?? string.Empty;
            var mat = reader["MaterialCode"]?.ToString() ?? string.Empty;

            return (lot, mat);
        }



        // ========== START SKID ==========
        // ======================= START SKID (unchanged logic) ==================
        public async Task<StartSkidResult> HandleStartSkidAsync(PressRunLogModel model, int pcsStart, string? overridePin = null)
        {
            var result = new StartSkidResult();

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            // Current mix for this machine
            var mixByMachine = await GetCurrentMixForMachineAsync(model.Machine);
            string mixLot = mixByMachine?.LotNumber;
            string mixCode = mixByMachine?.MaterialCode;

            // Scheduled material
            var scheduledCode = await GetScheduledMaterialCodeAsync(conn, model.Part ?? "", model.ProdNumber ?? "", model.Run ?? "");
            var normSched = NormalizeMaterial(scheduledCode);
            var normScan = NormalizeMaterial(mixCode);

            bool mismatch = !string.IsNullOrEmpty(normSched)
                            && !string.IsNullOrEmpty(normScan)
                            && !normSched.Equals(normScan, StringComparison.Ordinal);

            result.ScheduledMaterial = scheduledCode;
            result.ScannedMaterial = mixCode;

            bool isOverride = false;
            string? overrideBy = null;
            DateTime? overrideAt = null;
            if (mismatch)
            {
                // 🔹 Check for any prior override in *either* table for this part/prod/run
                var (hasOverride, existingSup) = await HasExistingOverrideAsync(
                    model.Part ?? "",
                    model.ProdNumber ?? "",
                    model.Run ?? ""
                );

                if (hasOverride)
                {
                    // ✅ Already overridden previously, reuse it
                    isOverride = true;
                    overrideBy = existingSup;
                    overrideAt = DateTime.Now;
                    result.OverrideUsed = true;
                    result.Supervisor = existingSup;
                    result.Code = "PRIOR_OVERRIDE";
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(overridePin))
                    {
                        result.RequiresOverride = true;
                        result.Code = "MATERIAL_MISMATCH";
                        result.Message = $"Material {mixCode ?? "(none)"} does not match scheduled {scheduledCode}. Supervisor override required.";
                        return result;
                    }

                    var (okPin, supName) = await VerifySupervisorPinAsync(overridePin);
                    if (!okPin)
                    {
                        result.RequiresOverride = true;
                        result.Code = "BAD_PIN";
                        result.Message = "Invalid supervisor PIN.";
                        return result;
                    }

                    isOverride = true;
                    overrideBy = supName;
                    overrideAt = DateTime.Now;
                    result.OverrideUsed = true;
                    result.Supervisor = supName;
                    result.Code = "OK";
                }
            }


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
    pcs         = (@pcsEnd - pcsStart),
    durationHours = ROUND(TIMESTAMPDIFF(SECOND, startDateTime, NOW()) / 3600, 2),
    runDate     = DATE(startDateTime),
    open        = 1
WHERE run = @run AND skidNumber = @skid AND endDateTime IS NULL
LIMIT 1;
";
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
               machine, prodNumber, skidNumber, pcsStart, lotNumber, materialCode,
               isOverride, overrideBy, overrideAt)
        VALUES (@run, @part, @component, NOW(), @operator,
                @machine, @prod, @skid, @pcsStart, @lotNumber, @materialCode,
                @isOverride, @overrideBy, @overrideAt);";

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
                ins.Parameters.AddWithValue("@lotNumber", (object?)mixLot ?? DBNull.Value);
                ins.Parameters.AddWithValue("@materialCode", (object?)mixCode ?? DBNull.Value);
                ins.Parameters.AddWithValue("@isOverride", isOverride);
                ins.Parameters.AddWithValue("@overrideBy", (object?)overrideBy ?? DBNull.Value);
                ins.Parameters.AddWithValue("@overrideAt", (object?)overrideAt ?? DBNull.Value);
                await ins.ExecuteNonQueryAsync();
            }

            result.SkidNumber = newSkidNumber;
            result.Message = $"Started skid {newSkidNumber}.";

            if (mismatch && result.OverrideUsed)
            {
                if (result.Code == "PRIOR_OVERRIDE")
                {
                    result.Message += $" (Material mismatch allowed by prior override from {result.Supervisor}).";
                }
                else
                {
                    result.Message += $" (Material mismatch overridden by {result.Supervisor}).";
                }
            }

            return result;
        }


        // =======================  LOGIN  =======================
        public async Task<LoginResult> HandleLoginAsync(PressRunLogModel m, string? overridePin = null)
        {
            var result = new LoginResult();

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // Current mix on this press
            var mixByMachine = await GetCurrentMixForMachineAsync(m.Machine);
            string mixLot = mixByMachine?.LotNumber;
            string mixCode = mixByMachine?.MaterialCode;

            // 🔹 Scheduled material for this part/prod/run
            var scheduledCode = await GetScheduledMaterialCodeAsync(conn, m.Part ?? "", m.ProdNumber ?? "", m.Run ?? "");
            var normSched = NormalizeMaterial(scheduledCode);
            var normScan = NormalizeMaterial(mixCode);

            bool mismatch = !string.IsNullOrEmpty(normSched)
                            && !string.IsNullOrEmpty(normScan)
                            && !normSched.Equals(normScan, StringComparison.Ordinal);

            result.ScheduledMaterial = scheduledCode;
            result.ScannedMaterial = mixCode;

            bool isOverride = false;
            string? overrideBy = null;
            DateTime? overrideAt = null;

            if (mismatch)
            {
                // 🔹 First: check if *any* prior override exists for this part/prod/run
                var (hasOverride, existingSup) = await HasExistingOverrideAsync(
                    m.Part ?? "",
                    m.ProdNumber ?? "",
                    m.Run ?? ""
                );

                if (hasOverride)
                {
                    // ✅ Already overridden somewhere (mixbagchange or pressrun), no new PIN
                    isOverride = true;
                    overrideBy = existingSup;
                    overrideAt = DateTime.Now; // or keep null if you want to preserve original timestamp
                    result.OverrideUsed = true;
                    result.Supervisor = existingSup;
                    result.Code = "PRIOR_OVERRIDE";
                }
                else
                {
                    // ❌ No prior override, so we need a PIN
                    if (string.IsNullOrWhiteSpace(overridePin))
                    {
                        await tx.RollbackAsync();
                        result.RequiresOverride = true;
                        result.Code = "MATERIAL_MISMATCH";
                        result.Message = $"Material {mixCode ?? "(none)"} does not match scheduled {scheduledCode}. Supervisor override required.";
                        return result;
                    }

                    var (okPin, supName) = await VerifySupervisorPinAsync(overridePin);
                    if (!okPin)
                    {
                        await tx.RollbackAsync();
                        result.RequiresOverride = true;
                        result.Code = "BAD_PIN";
                        result.Message = "Invalid supervisor PIN.";
                        return result;
                    }

                    isOverride = true;
                    overrideBy = supName;
                    overrideAt = DateTime.Now;
                    result.OverrideUsed = true;
                    result.Supervisor = supName;
                    result.Code = "OK";
                }
            }


            // 🔐 Auto-logout any open main run on this machine
            int pcsEndForPrev = m.PcsStart ?? 0;
            var auto = await AutoLogoutIfMachineOccupiedAsync(conn, (MySqlTransaction)tx, m.Machine, pcsEndForPrev, m.Operator);

            // Always create the main run record (Skid 0)
            const string insertMain = @"
        INSERT INTO pressrun
              (operator, part, component, machine, prodNumber, run,
               startDateTime, skidNumber, lotNumber, materialCode,
               isOverride, overrideBy, overrideAt, scheduledMaterial)
        VALUES (@operator, @part, @component, @machine, @prod, @run,
                @start, 0, @lotNumber, @materialCode,
                @isOverride, @overrideBy, @overrideAt, @scheduledMaterial);";

            using (var cmd = new MySqlCommand(insertMain, conn, (MySqlTransaction)tx))
            {
                cmd.Parameters.AddWithValue("@operator", m.Operator);
                cmd.Parameters.AddWithValue("@part", m.Part);
                cmd.Parameters.AddWithValue("@component", m.Component);
                cmd.Parameters.AddWithValue("@machine", m.Machine);
                cmd.Parameters.AddWithValue("@prod", m.ProdNumber);
                cmd.Parameters.AddWithValue("@run", m.Run);
                cmd.Parameters.AddWithValue("@start", m.StartDateTime);
                cmd.Parameters.AddWithValue("@lotNumber", (object?)mixLot ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@materialCode", (object?)mixCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@isOverride", isOverride);
                cmd.Parameters.AddWithValue("@overrideBy", (object?)overrideBy ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@overrideAt", (object?)overrideAt ?? DBNull.Value);

                cmd.Parameters.AddWithValue("@scheduledMaterial", (object?)scheduledCode ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            // Skid logic (same as you had, but add override fields on insert)
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
                    bool isOpenSkid = !rdr.IsDBNull("open") && rdr.GetBoolean("open");
                    if (skid > maxSkid) maxSkid = skid;
                    if (isOpenSkid) { openSkidNumber = skid; allClosed = false; }
                }
            }

            const string insertSkid = @"
        INSERT INTO pressrun
              (run, part, component, startDateTime, operator,
               machine, prodNumber, skidNumber, pcsStart, lotNumber, materialCode,
               isOverride, overrideBy, overrideAt, scheduledMaterial)
        VALUES (@run, @part, @component, NOW(), @operator,
                @machine, @prodNumber, @skid, @pcsStart, @lotNumber, @materialCode,
                @isOverride, @overrideBy, @overrideAt, @scheduledMaterial);";

            if (allClosed)
            {
                int newSkid = maxSkid + 1;
                result.SkidNumber = newSkid;
                result.NewSkid = true;
                result.Message = $"Logged in and started skid {newSkid}.";

                using var newSkidCmd = new MySqlCommand(insertSkid, conn, (MySqlTransaction)tx);
                newSkidCmd.Parameters.AddWithValue("@run", m.Run);
                newSkidCmd.Parameters.AddWithValue("@part", m.Part);
                newSkidCmd.Parameters.AddWithValue("@component", m.Component);
                newSkidCmd.Parameters.AddWithValue("@operator", m.Operator);
                newSkidCmd.Parameters.AddWithValue("@machine", m.Machine);
                newSkidCmd.Parameters.AddWithValue("@prodNumber", m.ProdNumber);
                newSkidCmd.Parameters.AddWithValue("@skid", newSkid);
                newSkidCmd.Parameters.AddWithValue("@pcsStart", m.PcsStart);
                newSkidCmd.Parameters.AddWithValue("@lotNumber", (object?)mixLot ?? DBNull.Value);
                newSkidCmd.Parameters.AddWithValue("@materialCode", (object?)mixCode ?? DBNull.Value);
                newSkidCmd.Parameters.AddWithValue("@isOverride", isOverride);
                newSkidCmd.Parameters.AddWithValue("@overrideBy", (object?)overrideBy ?? DBNull.Value);
                newSkidCmd.Parameters.AddWithValue("@overrideAt", (object?)overrideAt ?? DBNull.Value);
                newSkidCmd.Parameters.AddWithValue("@scheduledMaterial", (object?)scheduledCode ?? DBNull.Value);
                await newSkidCmd.ExecuteNonQueryAsync();
            }
            else
            {
                result.SkidNumber = openSkidNumber;
                result.NewSkid = false;
                result.Message = $"Logged in to existing skid {openSkidNumber}.";

                using var insert = new MySqlCommand(insertSkid, conn, (MySqlTransaction)tx);
                insert.Parameters.AddWithValue("@run", m.Run);
                insert.Parameters.AddWithValue("@part", m.Part);
                insert.Parameters.AddWithValue("@component", m.Component);
                insert.Parameters.AddWithValue("@operator", m.Operator);
                insert.Parameters.AddWithValue("@machine", m.Machine);
                insert.Parameters.AddWithValue("@prodNumber", m.ProdNumber);
                insert.Parameters.AddWithValue("@skid", openSkidNumber);
                insert.Parameters.AddWithValue("@pcsStart", m.PcsStart);
                insert.Parameters.AddWithValue("@lotNumber", (object?)mixLot ?? DBNull.Value);
                insert.Parameters.AddWithValue("@materialCode", (object?)mixCode ?? DBNull.Value);
                insert.Parameters.AddWithValue("@isOverride", isOverride);
                insert.Parameters.AddWithValue("@overrideBy", (object?)overrideBy ?? DBNull.Value);
                insert.Parameters.AddWithValue("@overrideAt", (object?)overrideAt ?? DBNull.Value);
                insert.Parameters.AddWithValue("@scheduledMaterial", (object?)scheduledCode ?? DBNull.Value);
                await insert.ExecuteNonQueryAsync();
            }

            if (auto.closed)
            {
                var who = string.IsNullOrWhiteSpace(auto.prevOperator) ? "previous operator" : auto.prevOperator;
                result.Message += $" (Auto-logged out {who} on machine {m.Machine})";
            }

            await tx.CommitAsync();

            // Print tag (unchanged)
            var latestRecord = await GetPressRunRecordAsync(conn, m.Run, result.SkidNumber);
            if (latestRecord != null)
            {
                string pdfFilePath = await GenerateRouterTagAsync(latestRecord);
                _sharedService.PrintFileToClosestPrinter(pdfFilePath, 1);
            }

            // If there was a mismatch and we used override, append to message
            if (mismatch && result.OverrideUsed)
            {
                if (result.Code == "PRIOR_OVERRIDE")
                {
                    result.Message += $" (Material mismatch allowed by prior override from {result.Supervisor}).";
                }
                else
                {
                    result.Message += $" (Material mismatch overridden by {result.Supervisor}).";
                }
            }

            return result;
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
    durationHours = ROUND(TIMESTAMPDIFF(SECOND, startDateTime, NOW()) / 3600, 2),
    runDate = DATE(startDateTime),
    scrap = @scrap,
    notes = @notes
WHERE id = @runId
  AND skidNumber = 0
LIMIT 1
";

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
    pcs = (@pcsEnd - pcsStart),
    durationHours = ROUND(TIMESTAMPDIFF(SECOND, startDateTime, NOW()) / 3600, 2),
    runDate = DATE(startDateTime),
    scrap = @scrap,
    notes = @notes,
    open = 1
WHERE run = @run
  AND skidNumber > 0
  AND endDateTime IS NULL
ORDER BY skidNumber DESC
LIMIT 1
";
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
    pcs = (@finalCount - pcsStart),
    durationHours = ROUND(TIMESTAMPDIFF(SECOND, startDateTime, NOW()) / 3600, 2),
    runDate = DATE(startDateTime),
    open = 1
WHERE run = @run
  AND skidNumber > 0
  AND endDateTime IS NULL
";
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
    durationHours = ROUND(TIMESTAMPDIFF(SECOND, startDateTime, NOW()) / 3600, 2),
    runDate = DATE(startDateTime),
    scrap = @scrap,
    notes = @notes
WHERE run = @run
  AND skidNumber = 0
LIMIT 1
";
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
            Directory.CreateDirectory(_exportsFolder); // ensure it exists
            string filePath = System.IO.Path.Combine(_exportsFolder, $"RouterTag_{model.Id}.pdf");


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

                // ----- Order of Ops header with inline barcodes -----

                // Create barcodes
                var prodBarcode = new Barcode128(pdf);
                prodBarcode.SetCode(model.ProdNumber ?? "");
                prodBarcode.SetCodeType(Barcode128.CODE128);
                Image prodBarcodeImg = new Image(prodBarcode.CreateFormXObject(pdf));

                var runBarcode = new Barcode128(pdf);
                runBarcode.SetCode(model.Run ?? "");
                runBarcode.SetCodeType(Barcode128.CODE128);
                Image runBarcodeImg = new Image(runBarcode.CreateFormXObject(pdf));

                // Make them smaller
                float barcodeScale = 0.6f;   // try 0.5 or 0.4 if you want even smaller

                prodBarcodeImg.SetAutoScale(false);
                prodBarcodeImg.Scale(barcodeScale, barcodeScale);
                prodBarcodeImg.SetHorizontalAlignment(HorizontalAlignment.CENTER);

                runBarcodeImg.SetAutoScale(false);
                runBarcodeImg.Scale(barcodeScale, barcodeScale);
                runBarcodeImg.SetHorizontalAlignment(HorizontalAlignment.CENTER);

                // 3-column table: Prod barcode (left) / title (center) / Run barcode (right)
                var headerTable1 = new Table(UnitValue.CreatePercentArray(new float[] { 2, 3, 2 }))
                    .UseAllAvailableWidth()
                    .SetMarginBottom(10);

                // LEFT: Prod # label directly above barcode, centered
                var leftCell = new Cell()
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.CENTER);

                leftCell.Add(new Paragraph($"Prod #: {model.ProdNumber}")
                    .SetFont(normalFont)
                    .SetFontSize(10)
                    .SetTextAlignment(TextAlignment.CENTER));

                leftCell.Add(prodBarcodeImg);
                headerTable1.AddCell(leftCell);

                // CENTER: "Order of Operations:"
                var middleCell = new Cell()
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetVerticalAlignment(VerticalAlignment.MIDDLE);

                middleCell.Add(new Paragraph("Order of Operations:")
                    .SetFont(boldFont)
                    .SetFontSize(14)
                    .SetTextAlignment(TextAlignment.CENTER));

                headerTable1.AddCell(middleCell);

                // RIGHT: Run label directly above barcode, centered
                var rightCell = new Cell()
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.CENTER);

                rightCell.Add(new Paragraph($"Run: {model.Run}")
                    .SetFont(normalFont)
                    .SetFontSize(10)
                    .SetTextAlignment(TextAlignment.CENTER));

                rightCell.Add(runBarcodeImg);
                headerTable1.AddCell(rightCell);

                // Add the whole header block
                document.Add(headerTable1);




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

                      

                        // --- Part factor details (lot & mix) ---
                        string pfMixLotLabel = "Mix Lot #";
                        string pfMixLotValue = null;
                        string pfMixNoLabel = "Mix No";
                        string pfMixNoValue = null;

                        DataTable partFactorDetails = await _sharedService
                            .GetLatestPartFactorDetailsAsync(qcc_file_desc, null, null);

                        if (partFactorDetails != null && partFactorDetails.Rows.Count >= 4)
                        {
                            // Row 2: Mix Lot #   | <value>
                            // Row 3: Mix No      | <value>
                            DataRow lotRow = partFactorDetails.Rows[2];
                            DataRow mixRow = partFactorDetails.Rows[3];

                            pfMixLotLabel = lotRow[0]?.ToString() ?? "Mix Lot #";
                            pfMixLotValue = lotRow[1]?.ToString() ?? "";

                            pfMixNoLabel = mixRow[0]?.ToString() ?? "Mix No";
                            pfMixNoValue = mixRow[1]?.ToString() ?? "";

                            // Optional: keep your little 4-cell header table if you like
                            Table headerTable3 = new Table(UnitValue.CreatePercentArray(new float[] { 1, 1, 1, 1 }))
                                .UseAllAvailableWidth()
                                .SetMarginBottom(10)
                                .SetBorder(Border.NO_BORDER);

                            headerTable3.AddCell(new Cell().Add(new Paragraph(pfMixLotLabel)
                                .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));
                            headerTable3.AddCell(new Cell().Add(new Paragraph(pfMixLotValue)
                                .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));

                            headerTable3.AddCell(new Cell().Add(new Paragraph(pfMixNoLabel)
                                .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));
                            headerTable3.AddCell(new Cell().Add(new Paragraph(pfMixNoValue)
                                .SetFont(normalFont).SetFontSize(12)).SetBorder(Border.NO_BORDER));

                            document.Add(headerTable3);
                        }
                        else
                        {
                            document.Add(new Paragraph("Not enough part-factor detail data available.")
                                .SetFont(normalFont).SetMarginBottom(10));
                        }


                        DataTable statistics = await _sharedService.GetStatisticsAsync(qcc_file_desc, model.StartDateTime);

                        // Fallbacks if part-factor data is missing
                        string lotForBarcode = !string.IsNullOrWhiteSpace(pfMixLotValue)
                            ? pfMixLotValue
                            : (string.IsNullOrWhiteSpace(model.LotNumber) ? "" : model.LotNumber);

                        string mixForBarcode = !string.IsNullOrWhiteSpace(pfMixNoValue)
                            ? pfMixNoValue
                            : (string.IsNullOrWhiteSpace(model.MaterialCode) ? "" : model.MaterialCode);

                        // Build barcodes only if we actually have values
                        Image lotBarcodeImg = null;
                        Image mixBarcodeImg = null;

                        if (!string.IsNullOrWhiteSpace(lotForBarcode))
                        {
                            var lotBarcode = new Barcode128(pdf);
                            lotBarcode.SetCode(lotForBarcode);
                            lotBarcode.SetCodeType(Barcode128.CODE128);
                            lotBarcodeImg = new Image(lotBarcode.CreateFormXObject(pdf))
                                .ScaleToFit(90, 25)
                                .SetHorizontalAlignment(HorizontalAlignment.CENTER);
                        }

                        if (!string.IsNullOrWhiteSpace(mixForBarcode))
                        {
                            var mixBarcode = new Barcode128(pdf);
                            mixBarcode.SetCode(mixForBarcode);
                            mixBarcode.SetCodeType(Barcode128.CODE128);
                            mixBarcodeImg = new Image(mixBarcode.CreateFormXObject(pdf))
                                .ScaleToFit(90, 25)
                                .SetHorizontalAlignment(HorizontalAlignment.CENTER);
                        }

                        // 3-column “Statistics” header row with barcodes inline
                        var statsHeader = new Table(UnitValue.CreatePercentArray(new float[] { 2, 3, 2 }))
                            .UseAllAvailableWidth()
                            .SetMarginTop(10)
                            .SetMarginBottom(10);

                        // LEFT: Lot (label + value) over barcode
                        var statsLeft = new Cell().SetBorder(Border.NO_BORDER)
                                                  .SetTextAlignment(TextAlignment.CENTER);
                        statsLeft.Add(new Paragraph($"{pfMixLotLabel}: {lotForBarcode}")
                            .SetFont(normalFont).SetFontSize(10)
                            .SetTextAlignment(TextAlignment.CENTER));
                        if (lotBarcodeImg != null)
                            statsLeft.Add(lotBarcodeImg);
                        statsHeader.AddCell(statsLeft);

                        // MIDDLE: “Statistics:”
                        var statsMiddle = new Cell().SetBorder(Border.NO_BORDER)
                                                    .SetTextAlignment(TextAlignment.CENTER)
                                                    .SetVerticalAlignment(VerticalAlignment.MIDDLE);
                        statsMiddle.Add(new Paragraph("Statistics:")
                            .SetFont(boldFont)
                            .SetFontSize(14)
                            .SetTextAlignment(TextAlignment.CENTER));
                        statsHeader.AddCell(statsMiddle);

                        // RIGHT: Mix (label + value) over barcode
                        var statsRight = new Cell().SetBorder(Border.NO_BORDER)
                                                   .SetTextAlignment(TextAlignment.CENTER);
                        statsRight.Add(new Paragraph($"{pfMixNoLabel}: {mixForBarcode}")
                            .SetFont(normalFont).SetFontSize(10)
                            .SetTextAlignment(TextAlignment.CENTER));
                        if (mixBarcodeImg != null)
                            statsRight.Add(mixBarcodeImg);
                        statsHeader.AddCell(statsRight);

                        document.Add(statsHeader);



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

                    float topHeaderOffset = 40f; // tweak this until it looks perfect

                    header.ShowTextAligned(
                        part + "  Skid # " + model.SkidNumber,
                        pageSize.GetWidth() / 2,
                        pageSize.GetTop() - topHeaderOffset,
                        TextAlignment.CENTER,
                        (float)Math.PI);
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
       operator, machine, pcsStart, pcsEnd, scrap, notes, skidNumber,pcs,
durationHours,
runDate,

       lotNumber, materialCode, isOverride, overrideBy, overrideAt,
       scheduledMaterial

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

        private static int TryOrdinal(DbDataReader rdr, string name)
        {
            try { return rdr.GetOrdinal(name); } catch { return -1; }
        }

        private PressRunLogModel ParseRunFromReader(DbDataReader rdr)
        {
            var pcsOrd = TryOrdinal(rdr, "pcs");
            var durOrd = TryOrdinal(rdr, "durationHours");
            var dateOrd = TryOrdinal(rdr, "runDate");

            var tsOrd = TryOrdinal(rdr, "timestamp");
            var lotOrd = TryOrdinal(rdr, "lotNumber");
            var matOrd = TryOrdinal(rdr, "materialCode");
            var isOvOrd = TryOrdinal(rdr, "isOverride");
            var ovByOrd = TryOrdinal(rdr, "overrideBy");
            var ovAtOrd = TryOrdinal(rdr, "overrideAt");
            var schedOrd = TryOrdinal(rdr, "scheduledMaterial");

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
                Pcs = pcsOrd >= 0 && !rdr.IsDBNull(pcsOrd) ? Convert.ToInt32(rdr["pcs"]) : (int?)null,

                DurationHours = durOrd >= 0 && !rdr.IsDBNull(durOrd)
    ? Convert.ToDecimal(rdr["durationHours"])
    : (decimal?)null,

                RunDate = dateOrd >= 0 && !rdr.IsDBNull(dateOrd)
    ? Convert.ToDateTime(rdr["runDate"])
    : (DateTime?)null,

                Scrap = rdr.IsDBNull(rdr.GetOrdinal("scrap")) ? (int?)null : rdr.GetInt32("scrap"),
                Notes = rdr["notes"]?.ToString(),
                SkidNumber = rdr.IsDBNull(rdr.GetOrdinal("skidNumber")) ? 0 : rdr.GetInt32("skidNumber"),
                LotNumber = lotOrd >= 0 && !rdr.IsDBNull(lotOrd) ? rdr["lotNumber"]?.ToString() : null,
                MaterialCode = matOrd >= 0 && !rdr.IsDBNull(matOrd) ? rdr["materialCode"]?.ToString() : null,

                // 🔹 new override fields
                IsOverride = isOvOrd >= 0 && !rdr.IsDBNull(isOvOrd) && Convert.ToBoolean(rdr["isOverride"]),
                OverrideBy = ovByOrd >= 0 && !rdr.IsDBNull(ovByOrd) ? rdr["overrideBy"]?.ToString() : null,
                OverrideAt = ovAtOrd >= 0 && !rdr.IsDBNull(ovAtOrd) ? rdr.GetDateTime(ovAtOrd) : (DateTime?)null,


    ScheduledMaterial = schedOrd >= 0 && !rdr.IsDBNull(schedOrd)
        ? rdr["scheduledMaterial"]?.ToString()
        : null,
           
            };
            return model;
        }

        // ---------- Material helpers & override checks ----------

        private static string NormalizeMaterial(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim().ToUpperInvariant();
            s = s.Replace(" ", "");
            while (s.Contains("--"))
                s = s.Replace("--", "-");
            return s;
        }

        /// <summary>
        /// Get scheduled materialCode for this part/prod/run from schedule.
        /// </summary>
        private async Task<string?> GetScheduledMaterialCodeAsync(
            MySqlConnection conn,
            string part,
            string prodNumber,
            string run)
        {
            const string sql = @"
        SELECT materialCode
        FROM schedule
        WHERE part = @part
          AND prodNumber = @prod
          AND run = @run
        ORDER BY id DESC
        LIMIT 1;";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@part", part ?? "");
            cmd.Parameters.AddWithValue("@prod", prodNumber ?? "");
            cmd.Parameters.AddWithValue("@run", run ?? "");

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString();
        }

        /// <summary>
        /// Check if there is a prior override in pressmixbagchange for the same
        /// part/prod/run/materialCode.
        /// </summary>
        private async Task<(bool hasOverride, string? supervisor, DateTime? at)>
            GetExistingMixOverrideAsync(
                MySqlConnection conn,
                string part,
                string prodNumber,
                string run,
                string? materialCode)
        {
            if (string.IsNullOrWhiteSpace(materialCode))
                return (false, null, null);

            const string sql = @"
        SELECT overrideBy, overrideAt
        FROM pressmixbagchange
        WHERE part = @part
          AND prodNumber = @prod
          AND run = @run
          AND isOverride = 1
          AND materialCode = @mat
        ORDER BY overrideAt DESC
        LIMIT 1;";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@part", part ?? "");
            cmd.Parameters.AddWithValue("@prod", prodNumber ?? "");
            cmd.Parameters.AddWithValue("@run", run ?? "");
            cmd.Parameters.AddWithValue("@mat", materialCode ?? "");

            await using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (!await rdr.ReadAsync())
                return (false, null, null);

            var sup = rdr["overrideBy"]?.ToString();
            DateTime? at = rdr.IsDBNull(rdr.GetOrdinal("overrideAt"))
                ? (DateTime?)null
                : rdr.GetDateTime("overrideAt");

            return (true, sup, at);
        }

        /// <summary>
        /// Uses the same supervisors PIN logic as PressMixBagChangeService.
        /// </summary>
        private async Task<(bool ok, string? name)> VerifySupervisorPinAsync(string pin)
        {
            if (string.IsNullOrWhiteSpace(pin))
                return (false, null);

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            const string sql = @"SELECT name FROM supervisors WHERE pin = @pin LIMIT 1;";
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@pin", pin);

            var result = await cmd.ExecuteScalarAsync();
            if (result == null) return (false, null);
            return (true, result.ToString());
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
       operator, machine, pcsStart, pcsEnd, scrap, notes, skidNumber,pcs,
durationHours,
runDate,

       lotNumber, materialCode, isOverride, overrideBy, overrideAt,
       scheduledMaterial
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

            // 🔹 include lotNumber & materialCode (and override fields if you want)
            const string cols = @"
        id,
        timestamp,
        prodNumber,
        run,
        part,
        component,
        startDateTime,
        endDateTime,pcs,
durationHours,
runDate,

        operator,
        machine,
        pcsStart,
        pcsEnd,
        scrap,
        notes,
        skidNumber,
        lotNumber,
        materialCode,
        isOverride,
        overrideBy,
        overrideAt,
       scheduledMaterial

";

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

                using (var rdr = await listCmd.ExecuteReaderAsync())
                {
                    while (await rdr.ReadAsync())
                        rows.Add(ParseRunFromReader(rdr));
                }

                // 🔹 Now that the reader is closed, we can reuse the same conn
                foreach (var row in rows)
                {
                    row.ScheduledMaterial = await GetScheduledMaterialCodeAsync(
                        conn,
                        row.Part ?? "",
                        row.ProdNumber ?? "",
                        row.Run ?? ""
                    );
                }

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
    durationHours = ROUND(TIMESTAMPDIFF(SECOND, startDateTime, NOW()) / 3600, 2),
    runDate = DATE(startDateTime),
    notes = CONCAT(IFNULL(notes,''), 
           CASE WHEN IFNULL(notes,'') = '' THEN '' ELSE ' ' END,
           '[Auto-logout by ', @newOp, ' ', DATE_FORMAT(NOW(),'%Y-%m-%d %H:%i'), ']')
WHERE id = @id
LIMIT 1;
;";
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
    pcs = (@pcsEnd - pcsStart),
    durationHours = ROUND(TIMESTAMPDIFF(SECOND, startDateTime, NOW()) / 3600, 2),
    runDate = DATE(startDateTime),
    open = 1,
    notes = CONCAT(IFNULL(notes,''), 
           CASE WHEN IFNULL(notes,'') = '' THEN '' ELSE ' ' END,
           '[Auto-logout main closed]')
WHERE run = @run
  AND skidNumber > 0
  AND endDateTime IS NULL
ORDER BY skidNumber DESC
LIMIT 1;
;";
            using (var cmd = new MySqlCommand(closeSkid, conn, tx))
            {
                cmd.Parameters.AddWithValue("@pcsEnd", pcsEndForPrev);
                cmd.Parameters.AddWithValue("@run", prevRun ?? "");
                await cmd.ExecuteNonQueryAsync();
            }

            return (true, prevOperator, prevRun);
        }
    
        public async Task<List<PressRunLogModel>> GetAllRunsAsync()
        {
            var list = new List<PressRunLogModel>();

            const string sql = @"
    SELECT 
        id,
        timestamp,
        prodNumber,
        run,
        part,
        component,
        startDateTime,
        endDateTime,
        operator,
        machine,
        pcsStart,pcs,
durationHours,
runDate,

        pcsEnd,
        scrap,
        notes,
        skidNumber,
        lotNumber,
        materialCode,
        isOverride,
        overrideBy,
        overrideAt,
       scheduledMaterial

    FROM pressrun
    ORDER BY startDateTime DESC;";


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

        #endregion

        public async Task<(bool hasOverride, string supervisorName)> HasExistingOverrideAsync(
    string part,
    string prodNumber,
    string run)
        {
            static string S(object? o) => o?.ToString() ?? "";

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            const string sql = @"
        SELECT overrideBy, overrideAt
        FROM (
            SELECT overrideBy, overrideAt
            FROM pressmixbagchange
            WHERE part = @part
              AND prodNumber = @prod
              AND run = @run
              AND isOverride = 1

            UNION ALL

            SELECT overrideBy, overrideAt
            FROM pressrun
            WHERE part = @part
              AND prodNumber = @prod
              AND run = @run
              AND isOverride = 1
        ) x
        WHERE overrideBy IS NOT NULL AND overrideBy <> ''
        ORDER BY overrideAt DESC
        LIMIT 1;";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@part", part ?? "");
            cmd.Parameters.AddWithValue("@prod", prodNumber ?? "");
            cmd.Parameters.AddWithValue("@run", run ?? "");

            await using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (await rdr.ReadAsync())
            {
                var sup = S(rdr["overrideBy"]);
                if (!string.IsNullOrWhiteSpace(sup))
                    return (true, sup);
            }

            return (false, "");
        }

       
        public async Task<HashSet<string>> GetOpenHoldKeysAsync(string? sourceFilter = null)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // dateCompleted IS NULL = active hold
            // skidNumber > 0 = only skids (not main run row)
            var sql = @"
SELECT source, ProdNumber, COALESCE(RunNumber,'') AS RunNumber, part, skidNumber
FROM holdrecords
WHERE dateCompleted IS NULL
  AND skidNumber > 0";

            if (!string.IsNullOrWhiteSpace(sourceFilter))
                sql += " AND source = @src";

            await using var conn = new MySqlConnection(_connectionStringMySQL);
            await conn.OpenAsync();

            await using var cmd = new MySqlCommand(sql, conn);
            if (!string.IsNullOrWhiteSpace(sourceFilter))
                cmd.Parameters.AddWithValue("@src", sourceFilter);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var source = r["source"]?.ToString() ?? "";
                var prod = r["ProdNumber"]?.ToString() ?? "";
                var run = r["RunNumber"]?.ToString() ?? "";
                var part = r["part"]?.ToString() ?? "";
                var skid = Convert.ToInt32(r["skidNumber"]);

                keys.Add(HoldKeyHelper.HoldKey(source, prod, run, part, skid));
            }

            return keys;
        }

    }
}
