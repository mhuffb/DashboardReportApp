using DashboardReportApp.Models;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using Mysqlx.Crud;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DashboardReportApp.Services
{
    public class DeviationService
    {
        private readonly string _conn;
        private readonly string _baseFolder;
        private readonly SharedService _sharedService;

        public DeviationService(
            IConfiguration configuration,
            SharedService sharedService,
            IOptionsMonitor<PathOptions> pathOptions,
            IWebHostEnvironment env)
        {
            _conn = configuration.GetConnectionString("MySQLConnection");
            _sharedService = sharedService;

            var configured = pathOptions.CurrentValue.DeviationUploads;
            _baseFolder = Path.IsPathFullyQualified(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));
            Directory.CreateDirectory(_baseFolder);
        }

        // Turn stored filename into absolute path
        public string GetAbsolutePath(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return "";

            // If DB still has a full path and it exists, use it as-is.
            if (Path.IsPathRooted(stored) && System.IO.File.Exists(stored))
                return stored;

            // Otherwise treat it as a filename and try the new base folder first.
            var name = Path.GetFileName(stored);
            var primary = Path.Combine(_baseFolder, name);
            if (System.IO.File.Exists(primary)) return primary;

            // Fallbacks for legacy locations (add any others you used before)
            var fallbacks = new[]
            {
        @"\\SINTERGYDC2024\Vol1\VSP\Uploads",
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads")
    };

            foreach (var root in fallbacks)
            {
                var candidate = Path.Combine(root, name);
                if (System.IO.File.Exists(candidate)) return candidate;
            }

            return ""; // not found anywhere
        }


        // Retrieves a list of operators for the dropdown.
        public List<string> GetOperators()
        {
            var operators = new List<string>();
            string query = "SELECT DISTINCT name FROM operators ORDER BY name";

            using (var connection = new MySqlConnection(_conn))
            {
                connection.Open();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        operators.Add(reader["name"].ToString());
                    }
                }
            }
            return operators;
        }

        // Saves a new deviation record.
        // If a file is uploaded, saves it and stores its path in FileAddress1.
        public async Task SaveDeviationAsync(DeviationModel model, IFormFile? file)
        {
            if (file != null && file.Length > 0)
            {
                // Use the same file-saving logic as the hold tag.
                string filePath = SaveDeviationFile(file);
                model.FileAddress1 = filePath;
            }
            // FileAddress2 remains untouched (read-only)

            string query = @"
                INSERT INTO deviation 
                (part, sentDateTime, discrepancy, operator, commMethod, disposition, approvedBy, dateTimeCASTReview, FileAddress1, FileAddress2)
                VALUES (@part, @sentDateTime, @discrepancy, @operator, @commMethod, @disposition, @approvedBy, @dateTimeCASTReview, @FileAddress1, @FileAddress2)";

            using (var connection = new MySqlConnection(_conn))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@part", model.Part?.ToUpper());
                    command.Parameters.AddWithValue("@sentDateTime", DateTime.Now);
                    command.Parameters.AddWithValue("@discrepancy", model.Discrepancy);
                    command.Parameters.AddWithValue("@operator", model.Operator);
                    command.Parameters.AddWithValue("@commMethod", model.CommMethod);
                    command.Parameters.AddWithValue("@disposition", model.Disposition ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@approvedBy", model.ApprovedBy ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@dateTimeCASTReview", model.DateTimeCASTReview ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@FileAddress1", model.FileAddress1 ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@FileAddress2", model.FileAddress2 ?? (object)DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        // Synchronous file-saving method modeled after the hold tag version.
        public string SaveDeviationFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is null or empty.", nameof(file));

            Directory.CreateDirectory(_baseFolder);

            var ext = Path.GetExtension(file.FileName);
            var fileName = $"DeviationFile1_{DateTime.UtcNow.Ticks}{ext}";
            var finalPath = Path.Combine(_baseFolder, fileName);

            using var stream = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None);
            file.CopyTo(stream);

            // store just the filename
            return fileName;
        }




        // Retrieves all deviation records.
        public async Task<List<DeviationModel>> GetAllDeviationsAsync()
        {
            var records = new List<DeviationModel>();
            string query = "SELECT * FROM deviation ORDER BY id DESC";

            using (var connection = new MySqlConnection(_conn))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var record = new DeviationModel
                        {
                            Id = reader.GetInt32("id"),
                            Part = reader.IsDBNull(reader.GetOrdinal("part")) ? null : reader.GetString("part"),
                            SentDateTime = reader.IsDBNull(reader.GetOrdinal("sentDateTime")) ? (DateTime?)null : reader.GetDateTime("sentDateTime"),
                            Discrepancy = reader.IsDBNull(reader.GetOrdinal("discrepancy")) ? null : reader.GetString("discrepancy"),
                            Operator = reader.IsDBNull(reader.GetOrdinal("operator")) ? null : reader.GetString("operator"),
                            CommMethod = reader.IsDBNull(reader.GetOrdinal("commMethod")) ? null : reader.GetString("commMethod"),
                            Disposition = reader.IsDBNull(reader.GetOrdinal("disposition")) ? null : reader.GetString("disposition"),
                            ApprovedBy = reader.IsDBNull(reader.GetOrdinal("approvedBy")) ? null : reader.GetString("approvedBy"),
                            DateTimeCASTReview = reader.IsDBNull(reader.GetOrdinal("dateTimeCASTReview")) ? (DateTime?)null : reader.GetDateTime("dateTimeCASTReview"),
                            FileAddress1 = reader.IsDBNull(reader.GetOrdinal("FileAddress1"))
        ? null
        : Path.GetFileName(reader.GetString(reader.GetOrdinal("FileAddress1"))),
                            FileAddress2 = reader.IsDBNull(reader.GetOrdinal("FileAddress2"))
        ? null
        : Path.GetFileName(reader.GetString(reader.GetOrdinal("FileAddress2")))
                        };
                        records.Add(record);
                    }
                }
            }
            return records;
        }

        // Generates a PDF for the deviation, prints it, and returns its file path.
        public string GenerateAndPrintDeviationPdf(DeviationModel model)
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", $"Deviation_{model.Part}_{DateTime.Now:yyyyMMddHHmmss}.pdf");

            PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            PdfFont normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            using (var writer = new PdfWriter(filePath))
            using (var pdf = new PdfDocument(writer))
            using (var document = new Document(pdf))
            {
                Paragraph title = new Paragraph("IN-HOUSE TEMPORARY DEVIATION REQUEST")
                    .SetFont(boldFont)
                    .SetFontSize(18)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetBorderBottom(new SolidBorder(1));
                document.Add(title);
                document.Add(new Paragraph("\n"));

                Table table = new Table(2).UseAllAvailableWidth();
                table.AddCell(new Cell().Add(new Paragraph("Part:").SetFont(boldFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph(model.Part).SetFont(normalFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph("Discrepancy:").SetFont(boldFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph(model.Discrepancy).SetFont(normalFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph("Operator:").SetFont(boldFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph(model.Operator).SetFont(normalFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph("Comm Method:").SetFont(boldFont)).SetBorderBottom(new SolidBorder(1)));
                table.AddCell(new Cell().Add(new Paragraph(model.CommMethod).SetFont(normalFont)).SetBorderBottom(new SolidBorder(1)));
                document.Add(table);

                document.Add(new Paragraph("\nDisposition: ________________________________").SetFont(normalFont).SetFontSize(12));
                document.Add(new Paragraph("\nApproved By: ________________________________").SetFont(normalFont).SetFontSize(12));

                document.Add(new Paragraph("\nPLACE ONE COPY OF DEVIATION WITH PAPERWORK AND PLACE ONE IN QUALITY CONTROL")
                    .SetFont(boldFont)
                    .SetTextAlignment(TextAlignment.CENTER));
                document.Add(new Paragraph("REVIEWED BY C.A.S.T")
                    .SetFont(boldFont)
                    .SetTextAlignment(TextAlignment.CENTER));
            }
            Console.WriteLine("Printing file: " + filePath);
            _sharedService.PrintFileToSpecificPrinter("qc1", filePath, 1);
            return filePath;
        }


        // Updates FileAddress1 for an existing deviation record.
        // Uses the same file-saving logic as in Create.
        public async Task<bool> UpdateFileAddress1Async(int id, IFormFile file)
        {
            var fileName = SaveDeviationFile(file); // filename only
            using var connection = new MySqlConnection(_conn);
            await connection.OpenAsync();
            const string sql = "UPDATE deviation SET FileAddress1 = @f WHERE id = @id";
            using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@f", fileName);
            cmd.Parameters.AddWithValue("@id", id);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }

    }
}
