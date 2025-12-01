using DashboardReportApp.Models;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using iText.Layout.Borders;

namespace DashboardReportApp.Services
{
    public class HoldTagService
    {
        private readonly string _connectionString;
        private readonly IWebHostEnvironment _environment;
        private readonly string _uploadsRoot;
        private readonly string _exportsRoot;

        public HoldTagService(IWebHostEnvironment environment, IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection")
                                 ?? throw new InvalidOperationException("Missing MySQLConnection.");

            _environment = environment;

            _uploadsRoot = configuration["Paths:HoldTagUploads"]
                           ?? Path.Combine(environment.WebRootPath, "uploads");

            _exportsRoot = configuration["Paths:HoldTagExports"]
                           ?? Path.Combine(environment.WebRootPath, "exports");

            Directory.CreateDirectory(_uploadsRoot);
            Directory.CreateDirectory(_exportsRoot);

            Console.WriteLine($"[HoldTagService] HoldTagUploads root = {_uploadsRoot}");
            Console.WriteLine($"[HoldTagService] HoldTagExports root = {_exportsRoot}");
        }



        /// <summary>
        /// Save an uploaded file into HoldTag uploads. Returns the FILENAME ONLY for DB storage.
        /// </summary>
        public string SaveHoldFile(IFormFile file, int recordId, string prefix = "HoldTagFile1")
        {
            Console.WriteLine($"[HoldTag] Uploading to: {_uploadsRoot}");


            if (file == null || file.Length == 0)
                throw new ArgumentException("File is null or empty.", nameof(file));

            var ext = Path.GetExtension(file.FileName);
            var fileName = $"{prefix}_{recordId}_{DateTime.UtcNow.Ticks}{ext}";
            var fullPath = Path.Combine(_uploadsRoot, fileName);
            Console.WriteLine($"[HoldTag] Saving {file.FileName} as {fullPath}");

            using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            file.CopyTo(fs);

            // Return just the filename so the DB does not contain full paths
            return fileName;
        }

        /// <summary>
        /// Returns an absolute path to a stored file.
        /// Accepts either legacy absolute path or new filename.
        /// Returns empty string if not resolvable.
        /// </summary>
        public string GetAbsolutePath(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return "";

            var value = stored.Trim();

            // Undo the %5C from URL encoding just in case
            value = value.Replace("%5C", "\\", StringComparison.OrdinalIgnoreCase);

            // 1. Try to extract just the filename first
            string fileName = Path.GetFileName(value);

            // 2. If that failed or looks weird, but the string contains "HoldTag",
            //    grab everything from "HoldTag..." onward, e.g.
            //    "\SINTERGYDC2024Vol1VSPUploadsHoldTag1_431_.jpg" -> "HoldTag1_431_.jpg"
            if (string.IsNullOrWhiteSpace(fileName) || !fileName.StartsWith("HoldTag", StringComparison.OrdinalIgnoreCase))
            {
                var idx = value.IndexOf("HoldTag", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    fileName = value.Substring(idx);
                }
            }

            // 3. If we now have something, look for it in the uploads folder
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var uploadsPath = Path.Combine(_uploadsRoot, fileName);

                if (File.Exists(uploadsPath))
                    return uploadsPath;
            }

            // 4. As a last resort, if the stored value is itself a rooted path and exists, use it
            if (Path.IsPathRooted(value) && File.Exists(value))
                return value;

            // Nothing found
            return "";
        }



        public async Task<int> AddHoldRecordAsync(HoldTagModel record)
        {
            const string query = @"
INSERT INTO holdrecords 
(part, component, prodNumber, lotNumber, materialCode, runNumber,
 discrepancy, date, issuedBy, disposition, dispositionBy,
 reworkInstr, reworkInstrBy, quantity, quantityOnHold, unit,
 pcsScrapped, dateCompleted, fileAddress1, fileAddress2)
VALUES 
(@part, @component, @prodNumber, @lotNumber, @materialCode, @runNumber,
 @discrepancy, @date, @issuedBy, @disposition, @dispositionBy,
 @reworkInstr, @reworkInstrBy, @quantity, @quantityOnHold, @unit,
 @pcsScrapped, @dateCompleted, @fileAddress1, @fileAddress2);
SELECT LAST_INSERT_ID();";





            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);

            command.Parameters.AddWithValue("@part", record.Part);
            command.Parameters.AddWithValue("@component", (object?)record.Component ?? DBNull.Value);
            command.Parameters.AddWithValue("@prodNumber", (object?)record.ProdNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@lotNumber", (object?)record.LotNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@materialCode", (object?)record.MaterialCode ?? DBNull.Value);
            command.Parameters.AddWithValue("@runNumber", (object?)record.RunNumber ?? DBNull.Value);


            command.Parameters.AddWithValue("@discrepancy", record.Discrepancy);
            command.Parameters.AddWithValue("@date", record.Date);
            command.Parameters.AddWithValue("@issuedBy", record.IssuedBy);
            command.Parameters.AddWithValue("@disposition", (object?)record.Disposition ?? DBNull.Value);
            command.Parameters.AddWithValue("@dispositionBy", (object?)record.DispositionBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@reworkInstr", (object?)record.ReworkInstr ?? DBNull.Value);
            command.Parameters.AddWithValue("@reworkInstrBy", (object?)record.ReworkInstrBy ?? DBNull.Value);

            command.Parameters.AddWithValue("@quantity", record.Quantity);
            command.Parameters.AddWithValue("@quantityOnHold", (object?)record.QuantityOnHold ?? DBNull.Value);
            command.Parameters.AddWithValue("@unit", record.Unit);
            command.Parameters.AddWithValue("@pcsScrapped", (object?)record.PcsScrapped ?? DBNull.Value);
            command.Parameters.AddWithValue("@dateCompleted", (object?)record.DateCompleted ?? DBNull.Value);

            command.Parameters.AddWithValue("@fileAddress1", (object?)record.FileAddress1 ?? DBNull.Value);
            command.Parameters.AddWithValue("@fileAddress2", (object?)record.FileAddress2 ?? DBNull.Value);


            var newId = Convert.ToInt32(await command.ExecuteScalarAsync());
            record.Id = newId;
            return newId;
        }

        public string GenerateHoldTagPdf(HoldTagModel record)
        {
            var filePath = Path.Combine(_exportsRoot, $"HoldTag_{record.Id}.pdf");

            var boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            using var writer = new PdfWriter(filePath);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf);

            string prodNumber = string.IsNullOrWhiteSpace(record.ProdNumber) ? "N/A" : record.ProdNumber;
            string runNumber = string.IsNullOrWhiteSpace(record.RunNumber) ? "N/A" : record.RunNumber;
            string part = string.IsNullOrWhiteSpace(record.Part) ? "N/A" : record.Part;
            string component = string.IsNullOrWhiteSpace(record.Component) ? "N/A" : record.Component;
            string lotNumber = string.IsNullOrWhiteSpace(record.LotNumber) ? "N/A" : record.LotNumber;
            string materialCode = string.IsNullOrWhiteSpace(record.MaterialCode) ? "N/A" : record.MaterialCode;
            string discrepancy = string.IsNullOrWhiteSpace(record.Discrepancy) ? "N/A" : record.Discrepancy;
            string issuedBy = string.IsNullOrWhiteSpace(record.IssuedBy) ? "Unknown" : record.IssuedBy;
            string issuedDate = record.Date.HasValue ? record.Date.Value.ToString("MM/dd/yyyy") : "N/A";

            string unitDisplay = string.IsNullOrWhiteSpace(record.Unit) ? "" : record.Unit + "(s)";
            string qtyTagsText = record.Quantity.HasValue ? record.Quantity.Value.ToString() : "N/A";
            string qtyOnHoldText = record.QuantityOnHold.HasValue
                ? $"{record.QuantityOnHold.Value} {unitDisplay}"
                : "N/A";

            // Header
            document.Add(
                new Paragraph("HOLD TAG")
                    .SetFont(boldFont)
                    .SetFontSize(20)
                    .SetTextAlignment(TextAlignment.CENTER)
            );

            document.Add(new Paragraph($"ID: {record.Id}").SetFont(boldFont).SetFontSize(12).SetTextAlignment(TextAlignment.CENTER));
            document.Add(new Paragraph("\n"));

            // Production / Run / Part / Component / Lot / Material
            document.Add(new Paragraph("Production Info").SetFont(boldFont).SetFontSize(14));
            document.Add(new Paragraph($"Production #: {prodNumber}").SetFont(normalFont));
            document.Add(new Paragraph($"Run #: {runNumber}").SetFont(normalFont));
            document.Add(new Paragraph($"Part: {part}").SetFont(normalFont));
            document.Add(new Paragraph($"Component: {component}").SetFont(normalFont));
            document.Add(new Paragraph($"Lot #: {lotNumber}").SetFont(normalFont));
            document.Add(new Paragraph($"Material: {materialCode}").SetFont(normalFont));
            document.Add(new Paragraph("\n"));

            // Discrepancy
            document.Add(new Paragraph("Discrepancy").SetFont(boldFont).SetFontSize(14));
            document.Add(new Paragraph(discrepancy).SetFont(normalFont));
            document.Add(new Paragraph("\n"));

            // Quantities
            document.Add(new Paragraph("Quantities").SetFont(boldFont).SetFontSize(14));
            document.Add(new Paragraph($"Hold Tags: {qtyTagsText}").SetFont(normalFont));
            document.Add(new Paragraph($"Qty On Hold: {qtyOnHoldText}").SetFont(normalFont));
            document.Add(new Paragraph("\n"));

            // Issued info
            document.Add(new Paragraph("Issued").SetFont(boldFont).SetFontSize(14));
            document.Add(new Paragraph($"By: {issuedBy}").SetFont(normalFont));
            document.Add(new Paragraph($"Date: {issuedDate}").SetFont(normalFont));
            document.Add(new Paragraph("\n"));

            // Footer reminder
            document.Add(
                new Paragraph("Return form to QA Manager once completed.")
                    .SetFont(boldFont)
                    .SetFontSize(12)
                    .SetTextAlignment(TextAlignment.CENTER)
            );

            return filePath;
        }



        public async Task UpdateFileAddress1Async(int id, string filenameOnly)
        {
            const string sql = @"UPDATE holdrecords SET FileAddress1 = @FileAddress1 WHERE Id = @Id";
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@FileAddress1", filenameOnly);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<HoldTagModel>> GetAllHoldRecordsAsync()
        {
            var records = new List<HoldTagModel>();
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            const string query = "SELECT * FROM HoldRecords ORDER BY Id DESC;";
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var rec = new HoldTagModel
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Timestamp = reader.IsDBNull(reader.GetOrdinal("Timestamp")) ? null : reader.GetDateTime(reader.GetOrdinal("Timestamp")),
                    Part = reader.IsDBNull(reader.GetOrdinal("Part")) ? null : reader.GetString(reader.GetOrdinal("Part")),
                    Component = reader.IsDBNull(reader.GetOrdinal("Component"))
        ? null
        : reader.GetString(reader.GetOrdinal("Component")),
                    ProdNumber = reader.IsDBNull(reader.GetOrdinal("ProdNumber"))
    ? null
    : reader.GetString(reader.GetOrdinal("ProdNumber")),

                    Discrepancy = reader.IsDBNull(reader.GetOrdinal("Discrepancy")) ? null : reader.GetString(reader.GetOrdinal("Discrepancy")),
                    Date = reader.IsDBNull(reader.GetOrdinal("Date")) ? null : reader.GetDateTime(reader.GetOrdinal("Date")),
                    IssuedBy = reader.IsDBNull(reader.GetOrdinal("IssuedBy")) ? null : reader.GetString(reader.GetOrdinal("IssuedBy")),
                    Disposition = reader.IsDBNull(reader.GetOrdinal("Disposition")) ? null : reader.GetString(reader.GetOrdinal("Disposition")),
                    DispositionBy = reader.IsDBNull(reader.GetOrdinal("DispositionBy")) ? null : reader.GetString(reader.GetOrdinal("DispositionBy")),
                    ReworkInstr = reader.IsDBNull(reader.GetOrdinal("ReworkInstr")) ? null : reader.GetString(reader.GetOrdinal("ReworkInstr")),
                    ReworkInstrBy = reader.IsDBNull(reader.GetOrdinal("ReworkInstrBy")) ? null : reader.GetString(reader.GetOrdinal("ReworkInstrBy")),
                    Quantity = reader.IsDBNull(reader.GetOrdinal("Quantity")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("Quantity")),
                    Unit = reader.IsDBNull(reader.GetOrdinal("Unit")) ? null : reader.GetString(reader.GetOrdinal("Unit")),
                    PcsScrapped = reader.IsDBNull(reader.GetOrdinal("PcsScrapped")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("PcsScrapped")),
                    DateCompleted = reader.IsDBNull(reader.GetOrdinal("DateCompleted")) ? null : reader.GetDateTime(reader.GetOrdinal("DateCompleted")),
                    FileAddress1 = reader.IsDBNull(reader.GetOrdinal("FileAddress1")) ? null : reader.GetString(reader.GetOrdinal("FileAddress1")),
                    FileAddress2 = reader.IsDBNull(reader.GetOrdinal("FileAddress2")) ? null : reader.GetString(reader.GetOrdinal("FileAddress2")),
                    LotNumber = reader.IsDBNull(reader.GetOrdinal("LotNumber"))
    ? null : reader.GetString(reader.GetOrdinal("LotNumber")),
                    MaterialCode = reader.IsDBNull(reader.GetOrdinal("MaterialCode"))
    ? null : reader.GetString(reader.GetOrdinal("MaterialCode")),
                    RunNumber = reader.IsDBNull(reader.GetOrdinal("RunNumber"))
    ? null : reader.GetString(reader.GetOrdinal("RunNumber")),

                    QuantityOnHold = reader.IsDBNull(reader.GetOrdinal("QuantityOnHold"))
    ? (int?)null : reader.GetInt32(reader.GetOrdinal("QuantityOnHold")),

                };
                records.Add(rec);
            }

            return records;
        }
        public async Task<List<string>> GetLastProdNumbersForPartAsync(string part)
        {
            var result = new List<string>();

            const string sql = @"
SELECT ProdNumber
FROM schedule
WHERE Part = @Part
  AND ProdNumber IS NOT NULL
GROUP BY ProdNumber
ORDER BY MAX(Id) DESC
LIMIT 10;";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Part", part);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                    result.Add(reader.GetString(0));
            }

            return result;
        }
        // --- ADMIN HELPERS MOVED FROM AdminHoldTagService ---




        // Operator lists (for the admin dropdowns in the edit modal)
        public async Task<List<string>> GetIssuedByOperatorsAsync()
        {
            var operators = new List<string>();
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            const string query = "SELECT name FROM operators";
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                    operators.Add(reader.GetString(0));
            }
            return operators;
        }

        public async Task<List<string>> GetDispositionOperatorsAsync()
        {
            var operators = new List<string>();
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            const string query = "SELECT name FROM operators WHERE allowHoldDisp = 1";
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                    operators.Add(reader.GetString(0));
            }
            return operators;
        }

        public async Task<List<string>> GetReworkOperatorsAsync()
        {
            var operators = new List<string>();
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            const string query = "SELECT name FROM operators WHERE allowHoldRework = 1";
            await using var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                    operators.Add(reader.GetString(0));
            }
            return operators;
        }
        public async Task<bool> UpdateHoldRecordAsync(HoldTagModel model, IFormFile? file1, IFormFile? file2)
        {
            string fileName1 = Path.GetFileName(model.FileAddress1 ?? "");
            string fileName2 = Path.GetFileName(model.FileAddress2 ?? "");

            if (file1 is { Length: > 0 })
                fileName1 = SaveHoldFile(file1, model.Id, "HoldTagFile1");

            if (file2 is { Length: > 0 })
                fileName2 = SaveHoldFile(file2, model.Id, "HoldTagFile2");

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            const string query = @"
UPDATE HoldRecords
SET 
    Part = @Part,
    Component = @Component,
    ProdNumber = @ProdNumber,
    LotNumber = @LotNumber,
    MaterialCode = @MaterialCode,
    RunNumber = @RunNumber,
    Discrepancy = @Discrepancy,
    Date = @Date,
    IssuedBy = @IssuedBy,
    Disposition = @Disposition,
    DispositionBy = @DispositionBy,
    ReworkInstr = @ReworkInstr,
    ReworkInstrBy = @ReworkInstrBy,
    Quantity = @Quantity,
    QuantityOnHold = @QuantityOnHold,
    Unit = @Unit,
    PcsScrapped = @PcsScrapped,
    DateCompleted = @DateCompleted,
    FileAddress1 = @FileAddress1,
    FileAddress2 = @FileAddress2
WHERE Id = @Id";


            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", model.Id);
            command.Parameters.AddWithValue("@Part", (object?)model.Part ?? DBNull.Value);
            command.Parameters.AddWithValue("@Component", (object?)model.Component ?? DBNull.Value);
            command.Parameters.AddWithValue("@Discrepancy", (object?)model.Discrepancy ?? DBNull.Value);
            command.Parameters.AddWithValue("@Date", (object?)model.Date ?? DBNull.Value);
            command.Parameters.AddWithValue("@IssuedBy", (object?)model.IssuedBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@Disposition", (object?)model.Disposition ?? DBNull.Value);
            command.Parameters.AddWithValue("@DispositionBy", (object?)model.DispositionBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@ReworkInstr", (object?)model.ReworkInstr ?? DBNull.Value);
            command.Parameters.AddWithValue("@ReworkInstrBy", (object?)model.ReworkInstrBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@Quantity", (object?)model.Quantity ?? DBNull.Value);
            command.Parameters.AddWithValue("@Unit", (object?)model.Unit ?? DBNull.Value);
            command.Parameters.AddWithValue("@PcsScrapped", (object?)model.PcsScrapped ?? DBNull.Value);
            command.Parameters.AddWithValue("@DateCompleted", (object?)model.DateCompleted ?? DBNull.Value);
            command.Parameters.AddWithValue("@FileAddress1",
                string.IsNullOrWhiteSpace(fileName1) ? (object)DBNull.Value : fileName1);
            command.Parameters.AddWithValue("@FileAddress2",
                string.IsNullOrWhiteSpace(fileName2) ? (object)DBNull.Value : fileName2);
            command.Parameters.AddWithValue("@ProdNumber", (object?)model.ProdNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@LotNumber", (object?)model.LotNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@MaterialCode", (object?)model.MaterialCode ?? DBNull.Value);
            command.Parameters.AddWithValue("@RunNumber", (object?)model.RunNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@QuantityOnHold", (object?)model.QuantityOnHold ?? DBNull.Value);


            var rows = await command.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public string GetUploadFullPath(string fileName)
        {
            return Path.Combine(_uploadsRoot, fileName);
        }

        public string? GetExistingFilePath(string storedName)
        {
            return ResolveStoredPath(storedName);
        }




        public string GetExportFullPath(string fileName)
        {
            fileName = Path.GetFileName(fileName);
            Directory.CreateDirectory(_exportsRoot);
            return Path.Combine(_exportsRoot, fileName);
        }

        private string? ResolveStoredPath(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return null;

            // 1) LEGACY: DB contains full/absolute path (C:\..., \\server\..., etc.)
            if (Path.IsPathRooted(stored) && File.Exists(stored))
                return stored;

            // 2) NEW: DB contains just the filename
            var fileName = Path.GetFileName(stored);
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            var combined = Path.Combine(_uploadsRoot, fileName);
            return File.Exists(combined) ? combined : null;
        }
        public class HoldScanResult
        {
            public string Source { get; set; } = ""; // "sinter" or "press"
            public string? Part { get; set; }
            public string? Component { get; set; }
            public string? ProdNumber { get; set; }
            public string? RunNumber { get; set; }
            public string? LotNumber { get; set; }
            public string? MaterialCode { get; set; }
        }

        public async Task<HoldScanResult?> LookupByScanAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            // 1) Try as production number in sintering table
            const string sinterSql = @"
SELECT part, component, prodNumber, run, lotNumber, materialCode
FROM sinterrun
WHERE prodNumber = @code
ORDER BY id DESC
LIMIT 1;";

            await using (var scmd = new MySqlCommand(sinterSql, conn))
            {
                scmd.Parameters.AddWithValue("@code", code);
                await using var r = await scmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    return new HoldScanResult
                    {
                        Source = "sinter",
                        Part = r["part"] as string,
                        Component = r["component"] as string,
                        ProdNumber = r["prodNumber"] as string,
                        RunNumber = r["run"] as string,
                        LotNumber = r["lotNumber"] as string,
                        MaterialCode = r["materialCode"] as string
                    };
                }
            }

            // 2) Try as run number in pressrun table
            const string pressSql = @"
SELECT part, component, prodNumber, run, lotNumber, materialCode
FROM pressrun
WHERE run = @code
ORDER BY id DESC
LIMIT 1;";

            await using (var pcmd = new MySqlCommand(pressSql, conn))
            {
                pcmd.Parameters.AddWithValue("@code", code);
                await using var r = await pcmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    return new HoldScanResult
                    {
                        Source = "press",
                        Part = r["part"] as string,
                        Component = r["component"] as string,
                        ProdNumber = r["prodNumber"] as string,
                        RunNumber = r["run"] as string,
                        LotNumber = r["lotNumber"] as string,
                        MaterialCode = r["materialCode"] as string
                    };
                }
            }

            return null;
        }
        public async Task<HoldScanResult?> LookupByProdAsync(string prodNumber)
        {
            if (string.IsNullOrWhiteSpace(prodNumber))
                return null;

            prodNumber = prodNumber.Trim();

            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            // 1) Try SINTER table first (as requested)
            const string sinterSql = @"
SELECT part, component, prodNumber, run, lotNumber, materialCode
FROM sinterrun
WHERE prodNumber = @prod
ORDER BY id DESC
LIMIT 1;";

            await using (var sinterCmd = new MySqlCommand(sinterSql, conn))
            {
                sinterCmd.Parameters.AddWithValue("@prod", prodNumber);

                await using var r = await sinterCmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    return new HoldScanResult
                    {
                        Source = "sinter",
                        Part = r["part"] as string,
                        Component = r["component"] as string,
                        ProdNumber = r["prodNumber"] as string,
                        RunNumber = r["run"] as string,
                        LotNumber = r["lotNumber"] as string,
                        MaterialCode = r["materialCode"] as string
                    };
                }
            }

            // 2) Fallback: try PRESS table (in case it hasn't been sintered yet)
            const string pressSql = @"
SELECT part, component, prodNumber, run, lotNumber, materialCode
FROM pressrun
WHERE prodNumber = @prod
ORDER BY id DESC
LIMIT 1;";

            await using (var pressCmd = new MySqlCommand(pressSql, conn))
            {
                pressCmd.Parameters.AddWithValue("@prod", prodNumber);

                await using var r2 = await pressCmd.ExecuteReaderAsync();
                if (await r2.ReadAsync())
                {
                    return new HoldScanResult
                    {
                        Source = "press",
                        Part = r2["part"] as string,
                        Component = r2["component"] as string,
                        ProdNumber = r2["prodNumber"] as string,
                        RunNumber = r2["run"] as string,
                        LotNumber = r2["lotNumber"] as string,
                        MaterialCode = r2["materialCode"] as string
                    };
                }
            }

            // Nothing found in either table
            return null;
        }

        public async Task<HoldScanResult?> LookupByRunAsync(string runNumber)
        {
            if (string.IsNullOrWhiteSpace(runNumber))
                return null;

            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            const string sql = @"
SELECT part, component, prodNumber, run, lotNumber, materialCode
FROM pressrun
WHERE run = @run
ORDER BY id DESC
LIMIT 1;";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@run", runNumber);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return new HoldScanResult
                {
                    Source = "press",
                    Part = r["part"] as string,
                    Component = r["component"] as string,
                    ProdNumber = r["prodNumber"] as string,
                    RunNumber = r["run"] as string,
                    LotNumber = r["lotNumber"] as string,
                    MaterialCode = r["materialCode"] as string
                };
            }

            return null;
        }

    }
}
