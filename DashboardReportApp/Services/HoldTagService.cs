﻿using DashboardReportApp.Models;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace DashboardReportApp.Services
{
    public class HoldTagService
    {
        private readonly string _connectionString;
        private readonly string _uploadsBase;
        private readonly string _exportsBase;

        public HoldTagService(IConfiguration configuration,
                              IOptionsMonitor<PathOptions> pathOptions,
                              IWebHostEnvironment env)
        {
            _connectionString = configuration.GetConnectionString("MySQLConnection")
                                 ?? throw new InvalidOperationException("Missing MySQLConnection.");

            var po = pathOptions.CurrentValue ?? new PathOptions();

            _uploadsBase = ResolveBase(po.HoldTagUploads, env.ContentRootPath);
            _exportsBase = ResolveBase(po.HoldTagExports, env.ContentRootPath);

            Directory.CreateDirectory(_uploadsBase);
            Directory.CreateDirectory(_exportsBase);
        }

        private static string ResolveBase(string? configured, string contentRoot)
        {
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("Missing path in PathOptions for HoldTag.");

            return Path.IsPathFullyQualified(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(contentRoot, configured));
        }

        /// <summary>
        /// Save an uploaded file into HoldTag uploads. Returns the FILENAME ONLY for DB storage.
        /// </summary>
        public string SaveHoldFile(IFormFile file, int recordId, string prefix = "HoldTagFile1")
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is null or empty.", nameof(file));

            var ext = Path.GetExtension(file.FileName);
            var fileName = $"{prefix}_{recordId}_{DateTime.UtcNow.Ticks}{ext}";
            var fullPath = Path.Combine(_uploadsBase, fileName);

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
        public string GetUploadsAbsolutePath(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return "";

            // If legacy absolute/UNC path and it exists, use it
            if (Path.IsPathRooted(stored) && File.Exists(stored))
                return stored;

            // Otherwise treat as filename
            var fileName = Path.GetFileName(stored);
            var combined = Path.Combine(_uploadsBase, fileName);
            return File.Exists(combined) ? combined : "";
        }

        /// <summary>
        /// Throws with clear message if file cannot be found.
        /// </summary>
        public string GetExistingFilePath(string? stored)
        {
            var abs = GetUploadsAbsolutePath(stored);
            if (!string.IsNullOrWhiteSpace(abs) && File.Exists(abs))
                return abs;

            // Also try if 'stored' itself is a path and exists (defensive)
            if (!string.IsNullOrWhiteSpace(stored) && File.Exists(stored))
                return stored;

            throw new FileNotFoundException($"File not found for stored value: {stored}");
        }

        public async Task<int> AddHoldRecordAsync(HoldTagModel record)
        {
            const string query = @"
INSERT INTO holdrecords 
(part, discrepancy, date, issuedBy, disposition, dispositionBy, reworkInstr, reworkInstrBy, quantity, unit, pcsScrapped, dateCompleted, fileAddress1, fileAddress2)
VALUES (@part, @discrepancy, @date, @issuedBy, @disposition, @dispositionBy, @reworkInstr, @reworkInstrBy, @quantity, @unit, @pcsScrapped, @dateCompleted, @fileAddress1, @fileAddress2);
SELECT LAST_INSERT_ID();";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(query, connection);

            command.Parameters.AddWithValue("@part", record.Part);
            command.Parameters.AddWithValue("@discrepancy", record.Discrepancy);
            command.Parameters.AddWithValue("@date", record.Date);
            command.Parameters.AddWithValue("@issuedBy", record.IssuedBy);
            command.Parameters.AddWithValue("@disposition", (object?)record.Disposition ?? DBNull.Value);
            command.Parameters.AddWithValue("@dispositionBy", (object?)record.DispositionBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@reworkInstr", (object?)record.ReworkInstr ?? DBNull.Value);
            command.Parameters.AddWithValue("@reworkInstrBy", (object?)record.ReworkInstrBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@quantity", record.Quantity);
            command.Parameters.AddWithValue("@unit", record.Unit);
            command.Parameters.AddWithValue("@pcsScrapped", (object?)record.PcsScrapped ?? DBNull.Value);
            command.Parameters.AddWithValue("@dateCompleted", (object?)record.DateCompleted ?? DBNull.Value);

            // Store whatever is currently on the model (filename or null)
            command.Parameters.AddWithValue("@fileAddress1", (object?)record.FileAddress1 ?? DBNull.Value);
            command.Parameters.AddWithValue("@fileAddress2", (object?)record.FileAddress2 ?? DBNull.Value);

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync());
            record.Id = newId;
            return newId;
        }

        public string GenerateHoldTagPdf(HoldTagModel record)
        {
            var filePath = Path.Combine(_exportsBase, $"HoldTag_{record.Id}.pdf");

            var boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            using var writer = new PdfWriter(filePath);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf);

            document.Add(new Paragraph("Hold").SetFont(boldFont).SetFontSize(18).SetTextAlignment(TextAlignment.CENTER));
            document.Add(new Paragraph("\n"));
            document.Add(new Paragraph("ID:").SetFont(boldFont).SetFontSize(12));
            document.Add(new Paragraph((record.Id == 0 ? "N/A" : record.Id.ToString())).SetFont(normalFont).SetFontSize(12));
            document.Add(new Paragraph("\n"));
            document.Add(new Paragraph("Part Number:").SetFont(boldFont).SetFontSize(12));
            document.Add(new Paragraph(string.IsNullOrWhiteSpace(record.Part) ? "N/A" : record.Part).SetFont(normalFont).SetFontSize(12));
            document.Add(new Paragraph("\n"));
            document.Add(new Paragraph("Discrepancy:").SetFont(boldFont).SetFontSize(12));
            document.Add(new Paragraph(record.Discrepancy ?? "N/A").SetFont(normalFont).SetFontSize(12));
            document.Add(new Paragraph("\n"));
            document.Add(new Paragraph("Issued By:").SetFont(boldFont).SetFontSize(12));
            document.Add(new Paragraph(string.IsNullOrWhiteSpace(record.IssuedBy) ? "Unknown" : record.IssuedBy).SetFont(normalFont).SetFontSize(12));
            document.Add(new Paragraph("Issued Date:").SetFont(boldFont).SetFontSize(12));
            document.Add(new Paragraph($"{record.Date:MM/dd/yyyy}").SetFont(normalFont).SetFontSize(12));
            document.Add(new Paragraph("\n"));
            document.Add(new Paragraph("Corrective Action Needed: Yes ☐  No ☐").SetFont(boldFont).SetFontSize(12));
            document.Add(new Paragraph("\n"));
            var qtyUnit = $"{record.Quantity} {record.Unit}".Trim();
            document.Add(new Paragraph("Amount:").SetFont(boldFont).SetFontSize(12));
            document.Add(new Paragraph(string.IsNullOrWhiteSpace(qtyUnit) ? "N/A" : qtyUnit).SetFont(normalFont).SetFontSize(12));
            document.Add(new Paragraph("\n"));
            document.Add(new Paragraph("Return Form To QA Manager Once Completed")
                .SetFont(boldFont).SetFontSize(12).SetTextAlignment(TextAlignment.CENTER));

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
                    FileAddress2 = reader.IsDBNull(reader.GetOrdinal("FileAddress2")) ? null : reader.GetString(reader.GetOrdinal("FileAddress2"))
                };
                records.Add(rec);
            }

            return records;
        }
    }
}
